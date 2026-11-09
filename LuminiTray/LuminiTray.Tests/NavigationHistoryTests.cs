using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class NavigationHistoryTests
{
    [TestMethod]
    public void InitialState_IsEmpty_CannotGoBackOrForward()
    {
        var history = new NavigationHistoryManager();

        Assert.AreEqual(0, history.Count);
        Assert.IsNull(history.CurrentPage);
        Assert.IsFalse(history.CanGoBack);
        Assert.IsFalse(history.CanGoForward);
        Assert.IsNull(history.PeekBack());
        Assert.IsNull(history.PeekForward());
        Assert.IsNull(history.GoBack());
        Assert.IsNull(history.GoForward());
    }

    [TestMethod]
    public void SinglePush_CurrentPageSet_CannotGoBackOrForward()
    {
        var history = new NavigationHistoryManager();
        history.Push("PageDash");

        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("PageDash", history.CurrentPage);
        Assert.IsFalse(history.CanGoBack);
        Assert.IsFalse(history.CanGoForward);
        Assert.IsNull(history.PeekBack());
        Assert.IsNull(history.PeekForward());
    }

    [TestMethod]
    public void ConsecutiveDuplicatePush_Ignored()
    {
        var history = new NavigationHistoryManager();
        history.Push("PageDash");
        history.Push("PageDash");
        history.Push("PageDash");

        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("PageDash", history.CurrentPage);
        Assert.IsFalse(history.CanGoBack);
    }

    [TestMethod]
    public void MultiplePushes_CanGoBack_AndNavigateBack()
    {
        var history = new NavigationHistoryManager();
        history.Push("PageDash");
        history.Push("PageSystem");
        history.Push("PageSensors");

        Assert.AreEqual(3, history.Count);
        Assert.AreEqual("PageSensors", history.CurrentPage);
        Assert.IsTrue(history.CanGoBack);
        Assert.IsFalse(history.CanGoForward);
        Assert.AreEqual("PageSystem", history.PeekBack());

        string? back1 = history.GoBack();
        Assert.AreEqual("PageSystem", back1);
        Assert.AreEqual("PageSystem", history.CurrentPage);
        Assert.IsTrue(history.CanGoBack);
        Assert.IsTrue(history.CanGoForward);
        Assert.AreEqual("PageDash", history.PeekBack());
        Assert.AreEqual("PageSensors", history.PeekForward());

        string? back2 = history.GoBack();
        Assert.AreEqual("PageDash", back2);
        Assert.AreEqual("PageDash", history.CurrentPage);
        Assert.IsFalse(history.CanGoBack);
        Assert.IsTrue(history.CanGoForward);
        Assert.AreEqual("PageSystem", history.PeekForward());
    }

    [TestMethod]
    public void GoForward_RestoresSubsequentPages()
    {
        var history = new NavigationHistoryManager();
        history.Push("PageDash");
        history.Push("PageSystem");
        history.Push("PageSensors");

        history.GoBack(); // to PageSystem
        history.GoBack(); // to PageDash

        string? fwd1 = history.GoForward();
        Assert.AreEqual("PageSystem", fwd1);
        Assert.AreEqual("PageSystem", history.CurrentPage);
        Assert.IsTrue(history.CanGoBack);
        Assert.IsTrue(history.CanGoForward);

        string? fwd2 = history.GoForward();
        Assert.AreEqual("PageSensors", fwd2);
        Assert.AreEqual("PageSensors", history.CurrentPage);
        Assert.IsTrue(history.CanGoBack);
        Assert.IsFalse(history.CanGoForward);
    }

    [TestMethod]
    public void PushAfterGoBack_TruncatesForwardBranch()
    {
        var history = new NavigationHistoryManager();
        history.Push("PageDash");
        history.Push("PageSystem");
        history.Push("PageSensors");

        history.GoBack(); // Current: PageSystem (Forward: PageSensors)
        Assert.IsTrue(history.CanGoForward);

        // Branch off to PageOc
        history.Push("PageOc");

        Assert.AreEqual("PageOc", history.CurrentPage);
        Assert.IsFalse(history.CanGoForward);
        Assert.IsTrue(history.CanGoBack);
        Assert.AreEqual("PageSystem", history.PeekBack());

        // Back goes to PageSystem, then PageDash (PageSensors is gone)
        Assert.AreEqual("PageSystem", history.GoBack());
        Assert.AreEqual("PageDash", history.GoBack());
        Assert.IsFalse(history.CanGoBack);
    }

    [TestMethod]
    public void CapacityLimit_TrimsOldestEntries()
    {
        var history = new NavigationHistoryManager(maxCapacity: 5);
        for (int i = 1; i <= 10; i++)
        {
            history.Push($"Page{i}");
        }

        Assert.AreEqual(5, history.Count);
        Assert.AreEqual("Page10", history.CurrentPage);

        // Can go back 4 times up to Page6
        Assert.AreEqual("Page9", history.GoBack());
        Assert.AreEqual("Page8", history.GoBack());
        Assert.AreEqual("Page7", history.GoBack());
        Assert.AreEqual("Page6", history.GoBack());
        Assert.IsFalse(history.CanGoBack);
    }

    [TestMethod]
    public void Clear_ResetsEntireHistory()
    {
        var history = new NavigationHistoryManager();
        history.Push("PageDash");
        history.Push("PageSystem");

        history.Clear();

        Assert.AreEqual(0, history.Count);
        Assert.IsNull(history.CurrentPage);
        Assert.IsFalse(history.CanGoBack);
        Assert.IsFalse(history.CanGoForward);
    }

    [TestMethod]
    public void FriendlyPageTitle_MapsAllMajorPages()
    {
        Assert.AreEqual("Visão Geral", NavigationHistoryManager.GetFriendlyPageTitle("PageDash"));
        Assert.AreEqual("Sistema & Hardware", NavigationHistoryManager.GetFriendlyPageTitle("PageSystem"));
        Assert.AreEqual("Sensores em Tempo Real", NavigationHistoryManager.GetFriendlyPageTitle("PageSensors"));
        Assert.AreEqual("Desinstalador de Programas", NavigationHistoryManager.GetFriendlyPageTitle("PageUninstaller"));
        Assert.AreEqual("Supressor de Ruído", NavigationHistoryManager.GetFriendlyPageTitle("PageSuppressor"));
        Assert.AreEqual("Configurações", NavigationHistoryManager.GetFriendlyPageTitle("PageSettings"));
        Assert.AreEqual("UnknownXYZ", NavigationHistoryManager.GetFriendlyPageTitle("PageUnknownXYZ"));
    }
}
