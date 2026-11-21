#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <unknwn.h>
#include <xamlOM.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <atomic>
#include <algorithm>
#include <string>
#include <vector>

using namespace winrt::Windows::UI::Xaml;

// {7B8D4E21-9C4A-4B8F-A312-90D4E5F61234}
static const GUID CLSID_LuminiTaskbarDiag = {
    0x7B8D4E21, 0x9C4A, 0x4B8F, {0xA3, 0x12, 0x90, 0xD4, 0xE5, 0xF6, 0x12, 0x34}
};

static std::atomic<long> g_dllRef{0};
static std::atomic<bool> g_stopWatcher{false};
static HANDLE g_watcherThread = nullptr;

enum class HideBellMode { Never = 0, WhenInactive = 1, WhenInactiveAndNoDnd = 2, Always = 3 };

struct TaskbarSettings {
    bool hideVolume = false;
    bool hideNetwork = false;
    bool hideBattery = false;
    bool grayscaleBattery = false;
    bool hideMicrophone = false;
    bool hideGeolocation = false;
    bool hideStudioEffects = false;
    bool hideRecall = false;
    bool hideLanguageBar = false;
    bool hideLanguageSupplementary = false;
    HideBellMode hideBell = HideBellMode::Never;
    int showDesktopWidth = -1; // -1 = padrão
};

static TaskbarSettings g_settings;
static std::vector<winrt::weak_ref<XamlRoot>> g_xamlRoots;
static winrt::agile_ref<winrt::Windows::UI::Core::CoreDispatcher> g_uiDispatcher{nullptr};

struct BatteryState {
    winrt::weak_ref<Controls::TextBlock> textBlock;
    Media::Brush savedForeground{nullptr};
    int64_t token{0};
};
static std::vector<BatteryState> g_batteryStates;

struct DesktopWidthState {
    winrt::weak_ref<FrameworkElement> element;
    bool hasMin = false; double savedMin = 0;
    bool hasMax = false; double savedMax = 0;
};
static std::vector<DesktopWidthState> g_desktopStates;

struct MainStackTextState {
    winrt::weak_ref<FrameworkElement> iconEl;
    winrt::weak_ref<Controls::TextBlock> textBlock;
    int64_t textToken{0};
    int64_t visToken{0};
};
static std::vector<MainStackTextState> g_mainStackStates;

static winrt::weak_ref<FrameworkElement> g_bellElement;
static int64_t g_bellToken = 0;
static winrt::weak_ref<FrameworkElement> g_ccStack;
static winrt::event_token g_ccSizeToken{};

static DWORD ReadRegDword(HKEY root, const wchar_t* sub, const wchar_t* name, DWORD defVal) {
    DWORD val = defVal, sz = sizeof(val), type = 0;
    if (RegGetValueW(root, sub, name, RRF_RT_REG_DWORD, &type, &val, &sz) == ERROR_SUCCESS)
        return val;
    return defVal;
}

static void LoadSettingsFromRegistry() {
    const wchar_t* k = L"Software\\LuminiTray\\TaskbarTweaks";
    g_settings.hideVolume = ReadRegDword(HKEY_CURRENT_USER, k, L"HideVolume", 0) != 0;
    g_settings.hideNetwork = ReadRegDword(HKEY_CURRENT_USER, k, L"HideNetwork", 0) != 0;
    g_settings.hideBattery = ReadRegDword(HKEY_CURRENT_USER, k, L"HideBattery", 0) != 0;
    g_settings.grayscaleBattery = ReadRegDword(HKEY_CURRENT_USER, k, L"GrayscaleBattery", 0) != 0;
    g_settings.hideMicrophone = ReadRegDword(HKEY_CURRENT_USER, k, L"HideMicrophone", 0) != 0;
    g_settings.hideGeolocation = ReadRegDword(HKEY_CURRENT_USER, k, L"HideGeolocation", 0) != 0;
    g_settings.hideStudioEffects = ReadRegDword(HKEY_CURRENT_USER, k, L"HideStudioEffects", 0) != 0;
    g_settings.hideRecall = ReadRegDword(HKEY_CURRENT_USER, k, L"HideRecall", 0) != 0;
    g_settings.hideLanguageBar = ReadRegDword(HKEY_CURRENT_USER, k, L"HideLanguageBar", 0) != 0;
    g_settings.hideLanguageSupplementary = ReadRegDword(HKEY_CURRENT_USER, k, L"HideLanguageSupplementary", 0) != 0;
    DWORD bell = ReadRegDword(HKEY_CURRENT_USER, k, L"HideBellMode", 0);
    g_settings.hideBell = (bell <= 3) ? static_cast<HideBellMode>(bell) : HideBellMode::Never;
    int w = static_cast<int>(ReadRegDword(HKEY_CURRENT_USER, k, L"ShowDesktopWidth", 0xFFFFFFFF));
    g_settings.showDesktopWidth = (w >= 0 && w <= 800) ? w : -1;
}

template <typename Fn>
static FrameworkElement EnumChildren(FrameworkElement el, Fn&& fn) {
    if (!el) return nullptr;
    int n = Media::VisualTreeHelper::GetChildrenCount(el);
    for (int i = 0; i < n; ++i) {
        auto c = Media::VisualTreeHelper::GetChild(el, i).try_as<FrameworkElement>();
        if (c && fn(c)) return c;
    }
    return nullptr;
}

static FrameworkElement FindChildByName(FrameworkElement el, const wchar_t* name) {
    return EnumChildren(el, [name](FrameworkElement c) { return c.Name() == name; });
}

static FrameworkElement FindChildByClass(FrameworkElement el, const wchar_t* cls) {
    return EnumChildren(el, [cls](FrameworkElement c) { return winrt::get_class_name(c) == cls; });
}

static FrameworkElement GetParentByName(FrameworkElement el, const wchar_t* name) {
    auto p = el;
    while (p) {
        p = Media::VisualTreeHelper::GetParent(p).try_as<FrameworkElement>();
        if (p && p.Name() == name) return p;
    }
    return nullptr;
}

enum class TrayIconKind {
    Unknown, None, Volume, Network, Battery, Microphone, Geolocation, MicAndGeo,
    BellEmpty, BellEmptyDnd, BellFull, BellFullDnd, Language, StudioEffects, Recall
};

static TrayIconKind IdentifyIcon(std::wstring_view t) {
    if (t.empty()) return TrayIconKind::None;
    if (t.size() != 1) return TrayIconKind::Unknown;
    switch (t[0]) {
        case L'\uE74F': case L'\uE992': case L'\uE993': case L'\uE994':
        case L'\uE995': case L'\uEA85': case L'\uEBC5':
            return TrayIconKind::Volume;
        case L'\uE709': case L'\uE7F4': case L'\uE839': case L'\uE86C':
        case L'\uE86D': case L'\uE86E': case L'\uE86F': case L'\uE870':
        case L'\uEAA1': case L'\uEAA2': case L'\uEAA3': case L'\uEAA4':
        case L'\uEAA5': case L'\uEAA8': case L'\uEC1E': case L'\uEC3C':
        case L'\uEC3D': case L'\uEC3E': case L'\uEC3F': case L'\uF384':
        case L'\uF8C0': case L'\uF8C1': case L'\uF8C2': case L'\uF8C3':
        case L'\uF8C4': case L'\uF8C5': case L'\uF8C6': case L'\uF8C7':
        case L'\uF8C8': case L'\uF8C9': case L'\uF8CA': case L'\uF8CB':
        case L'\uF8CC':
            return TrayIconKind::Network;
        case L'\uEB17': case L'\uEC02': case L'\uF1E8':
            return TrayIconKind::Battery;
        case L'\uE361': case L'\uE720': case L'\uEC71':
            return TrayIconKind::Microphone;
        case L'\uE37A':
            return TrayIconKind::Geolocation;
        case L'\uF47F':
            return TrayIconKind::MicAndGeo;
        case L'\uF2A3': return TrayIconKind::BellEmpty;
        case L'\uF285': return TrayIconKind::BellEmptyDnd;
        case L'\uF2A5': return TrayIconKind::BellFull;
        case L'\uF2A8': return TrayIconKind::BellFullDnd;
        case L'\uE4D7': case L'\uE4D8': case L'\uE5BF': case L'\uE97E':
        case L'\uE97F': case L'\uE980': case L'\uE982': case L'\uE983':
        case L'\uE986': case L'\uE987': case L'\uE988': case L'\uEB90':
        case L'\uEE41': case L'\uEE42': case L'\uEE43': case L'\uEE44':
        case L'\uEE45': case L'\uEE75': case L'\uEE76':
            return TrayIconKind::Language;
        case L'\uEABC': return TrayIconKind::StudioEffects;
        case L'\uEC83': case L'\uEADD': case L'\uEB16': case L'\uEF97': case L'\uF1C6':
            return TrayIconKind::Recall;
        default:
            if ((t[0] >= 0xE3C1 && t[0] <= 0xE3CB) ||
                (t[0] >= 0xE408 && t[0] <= 0xE41D) ||
                (t[0] >= 0xEBA0 && t[0] <= 0xEBC0))
                return TrayIconKind::Battery;
            return TrayIconKind::Unknown;
    }
}

static void ApplyMainStackIcon(FrameworkElement iconEl) {
    auto c = FindChildByName(iconEl, L"ContainerGrid");
    c = FindChildByName(c, L"ContentPresenter");
    c = FindChildByName(c, L"ContentGrid");
    c = FindChildByClass(c, L"SystemTray.TextIconContent");
    c = FindChildByName(c, L"ContainerGrid");
    c = FindChildByName(c, L"Base");
    auto tb = FindChildByName(c, L"InnerTextBlock").try_as<Controls::TextBlock>();
    if (!tb) return;

    auto eval = [](Controls::TextBlock textBlock) {
        switch (IdentifyIcon(textBlock.Text())) {
            case TrayIconKind::Microphone: return g_settings.hideMicrophone;
            case TrayIconKind::Geolocation: return g_settings.hideGeolocation;
            case TrayIconKind::MicAndGeo: return g_settings.hideMicrophone && g_settings.hideGeolocation;
            case TrayIconKind::StudioEffects: return g_settings.hideStudioEffects;
            case TrayIconKind::Recall: return g_settings.hideRecall;
            default: return false;
        }
    };

    iconEl.Visibility(eval(tb) ? Visibility::Collapsed : Visibility::Visible);

    std::erase_if(g_mainStackStates, [](const MainStackTextState& s) { return !s.textBlock.get() || !s.iconEl.get(); });
    bool tracked = std::any_of(g_mainStackStates.begin(), g_mainStackStates.end(),
        [&](const MainStackTextState& s) { return s.textBlock.get() == tb && s.iconEl.get() == iconEl; });
    if (!tracked) {
        auto weakEl = winrt::make_weak(iconEl);
        auto weakTb = winrt::make_weak(tb);
        MainStackTextState st;
        st.iconEl = iconEl;
        st.textBlock = tb;
        st.textToken = tb.RegisterPropertyChangedCallback(
            Controls::TextBlock::TextProperty(),
            [weakEl, eval](DependencyObject sender, DependencyProperty) {
                auto t = sender.try_as<Controls::TextBlock>();
                auto el = weakEl.get();
                if (t && el) {
                    auto targetVis = eval(t) ? Visibility::Collapsed : Visibility::Visible;
                    if (el.Visibility() != targetVis) el.Visibility(targetVis);
                }
            });
        // O Windows 11 volta a definir Visibility = Visible no ícone do microfone quando um app abre o áudio;
        // este callback força imediatamente Collapsed novamente enquanto a opção de ocultar estiver ativa.
        st.visToken = iconEl.RegisterPropertyChangedCallback(
            UIElement::VisibilityProperty(),
            [weakTb, eval](DependencyObject sender, DependencyProperty) {
                auto el = sender.try_as<FrameworkElement>();
                auto t = weakTb.get();
                if (el && t && eval(t) && el.Visibility() != Visibility::Collapsed) {
                    el.Visibility(Visibility::Collapsed);
                }
            });
        g_mainStackStates.push_back(std::move(st));
    }
}

static void ApplyNonActivatableIcon(FrameworkElement iconEl) {
    auto cg = FindChildByName(FindChildByName(FindChildByName(iconEl, L"ContainerGrid"), L"ContentPresenter"), L"ContentGrid");
    bool hide = false;
    auto matched = EnumChildren(cg, [&hide](FrameworkElement child) {
        auto cls = winrt::get_class_name(child);
        if (cls == L"SystemTray.TextIconContent") {
            auto tb = FindChildByName(FindChildByName(FindChildByName(child, L"ContainerGrid"), L"Base"), L"InnerTextBlock").try_as<Controls::TextBlock>();
            if (!tb) return false;
            if (IdentifyIcon(tb.Text()) == TrayIconKind::Language) {
                hide = g_settings.hideLanguageSupplementary;
                return true;
            }
            return false;
        }
        if (cls == L"SystemTray.ImageIconContent") {
            hide = g_settings.hideLanguageSupplementary;
            return true;
        }
        if (cls == L"SystemTray.LanguageTextIconContent" || cls == L"SystemTray.LanguageImageIconContent") {
            hide = g_settings.hideLanguageBar;
            return true;
        }
        return false;
    });
    if (!matched) {
        if (Media::VisualTreeHelper::GetChildrenCount(iconEl) == 0)
            iconEl.Visibility(Visibility::Visible);
        return;
    }
    iconEl.Visibility(hide ? Visibility::Collapsed : Visibility::Visible);
}

static void ApplyBatteryGrayscale(FrameworkElement batteryContent) {
    std::erase_if(g_batteryStates, [](const BatteryState& s) { return !s.textBlock.get(); });
    bool gs = g_settings.grayscaleBattery;
    if (!gs && g_batteryStates.empty()) return;

    auto g = FindChildByName(batteryContent, L"ContainerGrid");
    g = FindChildByClass(g, L"Windows.UI.Xaml.Controls.StackPanel");
    g = FindChildByClass(g, L"Windows.UI.Xaml.Controls.Grid");
    if (!g) return;

    EnumChildren(g, [gs](FrameworkElement child) {
        auto tb = child.try_as<Controls::TextBlock>();
        if (!tb) return false;
        auto it = std::find_if(g_batteryStates.begin(), g_batteryStates.end(),
            [&](const BatteryState& s) { return s.textBlock.get() == tb; });
        bool managed = (it != g_batteryStates.end());
        if (gs && !managed) {
            auto fg = tb.ReadLocalValue(Controls::TextBlock::ForegroundProperty()).try_as<Media::Brush>();
            if (!fg) return false;
            BatteryState st;
            st.textBlock = tb;
            st.savedForeground = fg;
            st.token = tb.RegisterPropertyChangedCallback(
                Controls::TextBlock::ForegroundProperty(),
                [](DependencyObject sender, DependencyProperty) {
                    auto t = sender.try_as<Controls::TextBlock>();
                    if (!t) return;
                    auto newFg = t.ReadLocalValue(Controls::TextBlock::ForegroundProperty()).try_as<Media::Brush>();
                    if (!newFg) return;
                    for (auto& s : g_batteryStates) {
                        if (s.textBlock.get() == t) { s.savedForeground = newFg; break; }
                    }
                    t.as<DependencyObject>().ClearValue(Controls::TextBlock::ForegroundProperty());
                });
            g_batteryStates.push_back(std::move(st));
            tb.as<DependencyObject>().ClearValue(Controls::TextBlock::ForegroundProperty());
        } else if (!gs && managed) {
            tb.UnregisterPropertyChangedCallback(Controls::TextBlock::ForegroundProperty(), it->token);
            tb.Foreground(it->savedForeground);
            g_batteryStates.erase(it);
        }
        return false;
    });
}

static void UpdateControlCenterStack(FrameworkElement stackPanel) {
    double spacing = 0;
    if (auto sp = stackPanel.try_as<Controls::StackPanel>()) spacing = sp.Spacing();

    FrameworkElement pending = nullptr;
    double pendingLeft = 0;
    int hiddenRun = 0;

    EnumChildren(stackPanel, [&](FrameworkElement child) {
        if (winrt::get_class_name(child) != L"Windows.UI.Xaml.Controls.ContentPresenter") return false;
        auto icon = FindChildByName(child, L"SystemTrayIcon").try_as<Controls::Control>();
        if (!icon) return false;
        if (!icon.IsEnabled()) { hiddenRun++; return false; }
        if (pending) {
            if (pendingLeft == 0) pending.ClearValue(FrameworkElement::MarginProperty());
            else pending.Margin(Thickness{pendingLeft, 0, 0, 0});
        }
        pending = child;
        pendingLeft = -spacing * hiddenRun;
        hiddenRun = 0;
        return false;
    });

    if (pending) {
        double right = -spacing * hiddenRun;
        if (pendingLeft == 0 && right == 0) pending.ClearValue(FrameworkElement::MarginProperty());
        else pending.Margin(Thickness{pendingLeft, 0, right, 0});
    }

    if (auto cc = GetParentByName(stackPanel, L"ControlCenterButton"))
        cc.Visibility(pending ? Visibility::Visible : Visibility::Collapsed);
}

static void ApplyControlCenterIcon(FrameworkElement iconEl) {
    auto cg = FindChildByName(FindChildByName(iconEl, L"ContainerGrid"), L"ContentGrid");
    if (!cg) return;

    bool hide = false;
    auto content = FindChildByClass(cg, L"SystemTray.BatteryIconContent");
    if (content) {
        hide = g_settings.hideBattery;
        ApplyBatteryGrayscale(content);
    } else {
        content = FindChildByClass(cg, L"SystemTray.TextIconContent");
        if (!content) return;
        auto tb = FindChildByName(FindChildByName(FindChildByName(content, L"ContainerGrid"), L"Base"), L"InnerTextBlock").try_as<Controls::TextBlock>();
        if (!tb) return;
        switch (IdentifyIcon(tb.Text())) {
            case TrayIconKind::Volume: hide = g_settings.hideVolume; break;
            case TrayIconKind::Network: hide = g_settings.hideNetwork; break;
            case TrayIconKind::Battery: hide = g_settings.hideBattery; break;
            default: break;
        }
    }

    bool hidden = content.Visibility() == Visibility::Collapsed;
    if (hide != hidden) {
        content.Visibility(hide ? Visibility::Collapsed : Visibility::Visible);
        if (auto ctrl = iconEl.try_as<Controls::Control>()) ctrl.IsEnabled(!hide);
    }

    auto p = Media::VisualTreeHelper::GetParent(iconEl).try_as<FrameworkElement>();
    if (p && winrt::get_class_name(p) == L"Windows.UI.Xaml.Controls.ContentPresenter") {
        auto sp = Media::VisualTreeHelper::GetParent(p).try_as<FrameworkElement>();
        if (sp && winrt::get_class_name(sp) == L"Windows.UI.Xaml.Controls.StackPanel") {
            if (!g_ccStack.get()) {
                g_ccStack = sp;
                g_ccSizeToken = sp.SizeChanged([](winrt::Windows::Foundation::IInspectable const& s, SizeChangedEventArgs const&) {
                    if (auto panel = s.try_as<FrameworkElement>()) UpdateControlCenterStack(panel);
                });
            }
            UpdateControlCenterStack(sp);
        }
    }
}

static void ApplyBellIcon(FrameworkElement iconEl) {
    auto container = FindChildByName(iconEl, L"ContainerGrid");
    if (!container) return;
    bool hasCp = true;
    auto child = FindChildByName(container, L"ContentPresenter");
    if (!child) { hasCp = false; child = container; }
    auto txtContent = FindChildByClass(FindChildByName(child, L"ContentGrid"), L"SystemTray.TextIconContent");
    if (!txtContent) return;

    bool hide = false;
    if (g_settings.hideBell == HideBellMode::Always) {
        hide = true;
    } else if (g_settings.hideBell == HideBellMode::WhenInactive || g_settings.hideBell == HideBellMode::WhenInactiveAndNoDnd) {
        auto tb = FindChildByName(FindChildByName(FindChildByName(txtContent, L"ContainerGrid"), L"Base"), L"InnerTextBlock").try_as<Controls::TextBlock>();
        if (tb) {
            auto k = IdentifyIcon(tb.Text());
            if (k == TrayIconKind::BellEmpty) hide = true;
            else if (k == TrayIconKind::BellEmptyDnd) hide = (g_settings.hideBell != HideBellMode::WhenInactiveAndNoDnd);
        }
        if (!g_bellElement.get()) {
            g_bellElement = iconEl;
            g_bellToken = iconEl.RegisterPropertyChangedCallback(
                Automation::AutomationProperties::NameProperty(),
                [](DependencyObject s, DependencyProperty) {
                    if (auto el = s.try_as<FrameworkElement>()) ApplyBellIcon(el);
                });
        }
    } else if (g_settings.hideBell == HideBellMode::Never) {
        if (auto prevBell = g_bellElement.get()) {
            prevBell.UnregisterPropertyChangedCallback(Automation::AutomationProperties::NameProperty(), g_bellToken);
            g_bellElement = nullptr;
            g_bellToken = 0;
        }
    }

    txtContent.Visibility(hide ? Visibility::Collapsed : Visibility::Visible);
    if (auto cp = Media::VisualTreeHelper::GetParent(iconEl).try_as<FrameworkElement>()) {
        if (hide && hasCp) cp.MaxWidth(0);
        else cp.ClearValue(FrameworkElement::MaxWidthProperty());
    }
}

static void ApplyShowDesktop(FrameworkElement iconEl) {
    std::erase_if(g_desktopStates, [](const DesktopWidthState& s) { return !s.element.get(); });
    if (g_settings.showDesktopWidth < 0) {
        for (auto& st : g_desktopStates) {
            if (auto el = st.element.get()) {
                if (st.hasMin) el.MinWidth(st.savedMin); else el.ClearValue(FrameworkElement::MinWidthProperty());
                if (st.hasMax) el.MaxWidth(st.savedMax); else el.ClearValue(FrameworkElement::MaxWidthProperty());
            }
        }
        g_desktopStates.clear();
        return;
    }

    auto setWidth = [](FrameworkElement el, double w) {
        auto it = std::find_if(g_desktopStates.begin(), g_desktopStates.end(),
            [&](const DesktopWidthState& s) { return s.element.get() == el; });
        if (it == g_desktopStates.end()) {
            DesktopWidthState st;
            st.element = el;
            if (auto mn = el.ReadLocalValue(FrameworkElement::MinWidthProperty()).try_as<double>()) { st.hasMin = true; st.savedMin = *mn; }
            if (auto mx = el.ReadLocalValue(FrameworkElement::MaxWidthProperty()).try_as<double>()) { st.hasMax = true; st.savedMax = *mx; }
            g_desktopStates.push_back(std::move(st));
        }
        el.MinWidth(w);
        el.MaxWidth(w);
    };

    double w = g_settings.showDesktopWidth;
    setWidth(iconEl, w);
    if (auto stack = GetParentByName(iconEl, L"ShowDesktopStack")) setWidth(stack, w);
    if (auto cg = FindChildByName(iconEl, L"ContainerGrid")) setWidth(cg, w);
}

static void ApplyStack(FrameworkElement container, void (*fn)(FrameworkElement)) {
    if (!container) return;
    auto sp = FindChildByName(container, L"Content");
    sp = FindChildByName(sp, L"IconStack");
    sp = FindChildByClass(sp, L"Windows.UI.Xaml.Controls.ItemsPresenter");
    sp = FindChildByClass(sp, L"Windows.UI.Xaml.Controls.StackPanel");
    if (!sp) return;
    EnumChildren(sp, [fn](FrameworkElement cp) {
        if (auto icon = FindChildByName(cp, L"SystemTrayIcon")) fn(icon);
        return false;
    });
}

static void ApplyButtonStack(FrameworkElement btn, void (*fn)(FrameworkElement)) {
    if (!btn) return;
    auto sp = FindChildByClass(btn, L"Windows.UI.Xaml.Controls.Grid");
    sp = FindChildByName(sp, L"ContentPresenter");
    sp = FindChildByClass(sp, L"Windows.UI.Xaml.Controls.ItemsPresenter");
    sp = FindChildByClass(sp, L"Windows.UI.Xaml.Controls.StackPanel");
    if (!sp) return;
    EnumChildren(sp, [fn](FrameworkElement cp) {
        if (auto icon = FindChildByName(cp, L"SystemTrayIcon")) fn(icon);
        return false;
    });
}

static bool ApplyToXamlRoot(XamlRoot xr) {
    if (!xr) return false;
    auto root = xr.Content().try_as<FrameworkElement>();
    auto frame = FindChildByClass(root, L"SystemTray.SystemTrayFrame");
    auto grid = FindChildByName(frame, L"SystemTrayFrameGrid");
    if (!grid) return false;

    ApplyStack(FindChildByName(grid, L"MainStack"), ApplyMainStackIcon);
    ApplyStack(FindChildByName(grid, L"NonActivatableStack"), ApplyNonActivatableIcon);
    ApplyButtonStack(FindChildByName(grid, L"ControlCenterButton"), ApplyControlCenterIcon);
    ApplyButtonStack(FindChildByName(grid, L"NotificationCenterButton"), ApplyBellIcon);
    ApplyStack(FindChildByName(grid, L"ShowDesktopStack"), ApplyShowDesktop);
    return true;
}

static void ApplyAll() {
    LoadSettingsFromRegistry();
    std::erase_if(g_xamlRoots, [](const winrt::weak_ref<XamlRoot>& r) { return !r.get(); });
    for (auto& wr : g_xamlRoots) {
        if (auto xr = wr.get()) ApplyToXamlRoot(xr);
    }
}

static DWORD WINAPI RefreshWatcherProc(LPVOID) {
    HANDLE ev = CreateEventW(nullptr, FALSE, FALSE, L"Local\\LuminiTrayTaskbarTweaksRefresh_v4");
    if (!ev) return 0;
    while (!g_stopWatcher) {
        DWORD res = WaitForSingleObject(ev, 500);
        if (res == WAIT_OBJECT_0 && !g_stopWatcher) {
            try {
                if (auto disp = g_uiDispatcher.get()) {
                    disp.RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Normal, []() { ApplyAll(); });
                }
            } catch (...) {}
        }
    }
    CloseHandle(ev);
    return 0;
}

class DiagSite final : public IObjectWithSite, public IVisualTreeServiceCallback2 {
    std::atomic<long> _ref{1};
    IUnknown* _site{nullptr};
    IXamlDiagnostics* _diag{nullptr};
    IVisualTreeService3* _vts{nullptr};

public:
    ~DiagSite() {
        if (_vts) _vts->Release();
        if (_diag) _diag->Release();
        if (_site) _site->Release();
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == __uuidof(IObjectWithSite)) { *ppv = static_cast<IObjectWithSite*>(this); AddRef(); return S_OK; }
        if (riid == __uuidof(IVisualTreeServiceCallback) || riid == __uuidof(IVisualTreeServiceCallback2)) {
            *ppv = static_cast<IVisualTreeServiceCallback2*>(this); AddRef(); return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++_ref; }
    ULONG STDMETHODCALLTYPE Release() override { long c = --_ref; if (c == 0) delete this; return c; }

    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* pUnkSite) override {
        if (_vts) { _vts->Release(); _vts = nullptr; }
        if (_diag) { _diag->Release(); _diag = nullptr; }
        if (_site) { _site->Release(); _site = nullptr; }
        if (!pUnkSite) return S_OK;
        _site = pUnkSite; _site->AddRef();
        pUnkSite->QueryInterface(__uuidof(IXamlDiagnostics), reinterpret_cast<void**>(&_diag));
        pUnkSite->QueryInterface(__uuidof(IVisualTreeService3), reinterpret_cast<void**>(&_vts));
        LoadSettingsFromRegistry();
        if (!g_watcherThread) {
            g_stopWatcher = false;
            g_watcherThread = CreateThread(nullptr, 0, RefreshWatcherProc, nullptr, 0, nullptr);
        }
        if (_vts) _vts->AdviseVisualTreeChange(this);
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** ppv) override {
        return _site ? _site->QueryInterface(riid, ppv) : E_FAIL;
    }

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation, VisualElement element, VisualMutationType mutationType) override {
        if (mutationType != Add || !_diag) return S_OK;
        if (!element.Type || (wcscmp(element.Type, L"SystemTray.SystemTrayFrame") != 0
            && wcscmp(element.Type, L"SystemTray.IconView") != 0
            && wcscmp(element.Type, L"SystemTray.TextIconContent") != 0))
            return S_OK;

        IInspectable* raw = nullptr;
        if (SUCCEEDED(_diag->GetIInspectableFromHandle(element.Handle, &raw)) && raw) {
            FrameworkElement fe{nullptr};
            winrt::copy_from_abi(fe, raw);
            raw->Release();
            if (fe) {
                if (!g_uiDispatcher) {
                    try { g_uiDispatcher = fe.Dispatcher(); } catch (...) {}
                }
                if (auto xr = fe.XamlRoot()) {
                    bool known = std::any_of(g_xamlRoots.begin(), g_xamlRoots.end(),
                        [&](const winrt::weak_ref<XamlRoot>& r) { return r.get() == xr; });
                    if (!known) g_xamlRoots.push_back(winrt::make_weak(xr));
                    ApplyToXamlRoot(xr);
                }
            }
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) override {
        return S_OK;
    }
};

class DiagFactory final : public IClassFactory {
    std::atomic<long> _ref{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override {
        if (riid == IID_IUnknown || riid == IID_IClassFactory) { *ppv = this; AddRef(); return S_OK; }
        *ppv = nullptr; return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++_ref; }
    ULONG STDMETHODCALLTYPE Release() override { long c = --_ref; if (c == 0) delete this; return c; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* obj = new DiagSite();
        HRESULT hr = obj->QueryInterface(riid, ppv);
        obj->Release();
        return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL fLock) override {
        if (fLock) ++g_dllRef; else --g_dllRef;
        return S_OK;
    }
};

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv) {
    if (rclsid != CLSID_LuminiTaskbarDiag) return CLASS_E_CLASSNOTAVAILABLE;
    auto* f = new DiagFactory();
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

extern "C" HRESULT __stdcall DllCanUnloadNow() {
    return (g_dllRef == 0) ? S_OK : S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
    } else if (reason == DLL_PROCESS_DETACH) {
        g_stopWatcher = true;
        if (g_watcherThread) { WaitForSingleObject(g_watcherThread, 1000); CloseHandle(g_watcherThread); g_watcherThread = nullptr; }
    }
    return TRUE;
}
