namespace LuminiTray;

/// <summary>
/// Modelo de um componente com potência em Watts, categoria e plataforma de compatibilidade.
/// </summary>
public sealed record PsuComponentItem(string Name, int Watts, string Category, string Platform = "All")
{
    public override string ToString() => Name;
}

/// <summary>
/// Opção de memória RAM para cálculo de potência.
/// </summary>
public sealed record PsuRamOption(string Label, string Tag, string Generation, bool IsDefault = false)
{
    public override string ToString() => Label;
}

/// <summary>
/// Configuração de hardware informada ou detectada para o cálculo de fonte.
/// </summary>
public sealed class PsuConfig
{
    public string CpuName { get; set; } = "";
    public int CpuWatts { get; set; } = 80;

    public string MotherboardName { get; set; } = "";
    public int MotherboardWatts { get; set; } = 50;

    public string GpuName { get; set; } = "";
    public int GpuWatts { get; set; } = 200;

    public string RamType { get; set; } = "2x DDR4";
    public int RamSticks { get; set; } = 2;
    public int RamWattsOverride { get; set; } = 0;

    public int NvmeCount { get; set; } = 1;
    public int SataSsdCount { get; set; } = 0;
    public int HddCount { get; set; } = 0;

    public string CoolerType { get; set; } = "Air Cooler Padrão"; // Air Cooler, AIO 240mm, AIO 360mm
    public int FanCount { get; set; } = 4;
    public bool Overclock { get; set; } = false;
    public bool RgbAndPeripherals { get; set; } = true;
}

/// <summary>
/// Resultado detalhado do cálculo de potência e recomendação de fonte (PSU).
/// </summary>
public sealed record PsuCalculationResult(
    int CpuWatts,
    int GpuWatts,
    int MotherboardWatts,
    int RamWatts,
    int StorageWatts,
    int CoolingWatts,
    int UsbAndMiscWatts,
    int TotalPeakWatts,
    int RecommendedPsuWatts,
    string RecommendedEfficiency,
    string PcieCablesRecommendation,
    string UpgradeAdvice,
    int HeadroomWatts,
    double LoadPercentageAtRecommended
);

/// <summary>
/// Calculadora de Fonte (PSU) e simulador de upgrade de hardware para o Lumini Tray.
/// </summary>
public static class PsuCalculator
{
    public static readonly List<PsuRamOption> AllRamOptions = new()
    {
        // DDR5
        new("2x DDR5 (32GB/64GB · 12W)", "DDR5;2;12", "DDR5", true),
        new("4x DDR5 (64GB/128GB · 24W)", "DDR5;4;24", "DDR5"),
        new("1x DDR5 (16GB/32GB · 6W)", "DDR5;1;6", "DDR5"),
        new("2x DDR5 Alta Freq. / RGB (16W)", "DDR5;2;16", "DDR5"),
        new("4x DDR5 Alta Freq. / RGB (32W)", "DDR5;4;32", "DDR5"),
        new("2x DDR5 SO-DIMM (Notebook · 6W)", "DDR5;2;6", "DDR5"),
        new("1x DDR5 SO-DIMM (Notebook · 3W)", "DDR5;1;3", "DDR5"),

        // DDR4
        new("2x DDR4 (16GB/32GB · 8W)", "DDR4;2;8", "DDR4", true),
        new("4x DDR4 (32GB/64GB · 16W)", "DDR4;4;16", "DDR4"),
        new("1x DDR4 (8GB/16GB · 4W)", "DDR4;1;4", "DDR4"),
        new("8x DDR4 Quad/Octa (32W)", "DDR4;8;32", "DDR4"),
        new("2x DDR4 SO-DIMM (Notebook · 5W)", "DDR4;2;5", "DDR4"),
        new("1x DDR4 SO-DIMM (Notebook · 2.5W)", "DDR4;1;3", "DDR4"),

        // DDR3
        new("2x DDR3 (8GB/16GB · 6W)", "DDR3;2;6", "DDR3", true),
        new("4x DDR3 (16GB/32GB · 12W)", "DDR3;4;12", "DDR3"),
        new("1x DDR3 (4GB/8GB · 3W)", "DDR3;1;3", "DDR3"),
        new("2x DDR3L SO-DIMM (Notebook · 4W)", "DDR3;2;4", "DDR3"),

        // DDR2
        new("2x DDR2 (4GB/8GB · 5W)", "DDR2;2;5", "DDR2", true),
        new("4x DDR2 (8GB/16GB · 10W)", "DDR2;4;10", "DDR2"),
        new("2x DDR2 SO-DIMM (Notebook · 4W)", "DDR2;2;4", "DDR2")
    };

    public static readonly List<PsuComponentItem> MotherboardList = new()
    {
        // 1. Desktop Moderno / Intermediário (Mantém compatibilidade de testes)
        new("Placa-Mãe Intermediária B-Series (B450, B550, B650, B760, B660, B560)", 50, "Intermediária", "Intermediária"),
        new("Placa-Mãe Básica / Entrada (A320, A520, A620, H310, H410, H510, H610)", 35, "Básica", "Básica"),
        new("Placa-Mãe Avançada Z / X-Series (X570, X670, X870, Z690, Z790, Z890)", 75, "Avançada", "Avançada"),
        new("Placa-Mãe Entusiasta / Overclock (ROG Maximus, Crosshair, Godlike, Taichi)", 95, "Entusiasta", "Entusiasta"),
        new("Placa-Mãe Mini-ITX Compacta (Todos os chipsets Mini-ITX)", 35, "Mini-ITX", "Mini-ITX"),

        // 2. Plataformas Específicas por Socket Desktop
        new("AMD AM5 (B650, X670, X870, A620 - Ryzen 7000/8000/9000)", 50, "AM5", "AM5"),
        new("AMD AM4 (B450, B550, A320, A520, X570 - Ryzen 1000 a 5000)", 45, "AM4", "AM4"),
        new("Intel LGA 1700 (B760, Z790, B660, H610 - 12ª, 13ª e 14ª Gen)", 55, "LGA 1700", "LGA1700"),
        new("Intel LGA 1851 (Z890, B860 - Core Ultra 200)", 65, "LGA 1851", "LGA1851"),
        new("Intel LGA 1200 (B560, Z590, Z490, H510 - 10ª e 11ª Gen)", 50, "LGA 1200", "LGA1200"),
        new("Intel LGA 1151 (B360, Z390, Z370, H310, B250, H110 - 6ª a 9ª Gen)", 45, "LGA 1151", "LGA1151"),

        // 3. Plataformas Clássicas / Legado
        new("Placa-Mãe Clássica Intel LGA 1155 / 1150 (H61, B75, Z77, H81, B85, Z97)", 45, "Legado DDR3", "LGA1150_1155"),
        new("Placa-Mãe Clássica AMD AM3+ / AM3 / FM2+ (970, 990FX, 760G, A68H, A88X)", 50, "Clássica AMD", "AM3"),
        new("Placa-Mãe Legado Intel LGA 775 / DDR2 / DDR3 (G31, G41, P35, P45)", 40, "Legado LGA 775", "LGA775"),
        new("Placa-Mãe Legado Intel LGA 1156 / 1366 (H55, P55, X58)", 55, "Legado LGA 1366/1156", "LGA1366_1156"),
        new("Placa-Mãe Legado AMD AM2 / AM2+ (NF-520, NF-720, 780G, 790GX)", 45, "Legado AM2", "AM2"),
        new("Placa-Mãe Workstation / HEDT / Dual CPU (X99, X79, X299, TRX40, WRX80, Dual Xeon)", 110, "Workstation", "Workstation"),

        // 4. Plataformas de Notebook / Laptop
        new("Notebook Gamer Intel (12ª, 13ª, 14ª Gen / Core Ultra Mobile)", 35, "Notebook", "Notebook_Intel"),
        new("Notebook Gamer AMD (Ryzen 6000, 7000, 8000 Series Mobile)", 30, "Notebook", "Notebook_AMD"),
        new("Notebook Clássico Intel (4ª a 11ª Geração Mobile)", 25, "Notebook", "Notebook_Intel_Classic"),
        new("Notebook Clássico AMD (Ryzen 3000, 4000, 5000 Mobile)", 25, "Notebook", "Notebook_AMD_Classic"),
        new("Notebook Básico / Ultrafino (Intel U-Series / AMD U-Series)", 15, "Notebook", "Notebook_Basic"),
        new("Todas as Placas-Mãe (Desktop & Notebook - Sem Filtro)", 50, "Todas", "All")
    };

    public static readonly List<PsuComponentItem> CpuList = new()
    {
        // AMD Ryzen 9000 (Zen 5 - AM5)
        new("AMD Ryzen 9 9950X (16C/32T)", 230, "AMD", "AM5"),
        new("AMD Ryzen 9 9900X (12C/24T)", 200, "AMD", "AM5"),
        new("AMD Ryzen 7 9800X3D (8C/16T)", 135, "AMD", "AM5"),
        new("AMD Ryzen 7 9700X (8C/16T)", 142, "AMD", "AM5"),
        new("AMD Ryzen 5 9600X (6C/12T)", 120, "AMD", "AM5"),

        // AMD Ryzen 7000 / 8000 (Zen 4 - AM5)
        new("AMD Ryzen 9 7950X3D (16C/32T)", 162, "AMD", "AM5"),
        new("AMD Ryzen 9 7950X (16C/32T)", 230, "AMD", "AM5"),
        new("AMD Ryzen 9 7900X3D (12C/24T)", 162, "AMD", "AM5"),
        new("AMD Ryzen 9 7900X (12C/24T)", 210, "AMD", "AM5"),
        new("AMD Ryzen 9 7900 (12C/24T)", 90, "AMD", "AM5"),
        new("AMD Ryzen 7 7800X3D (8C/16T)", 120, "AMD", "AM5"),
        new("AMD Ryzen 7 7700X (8C/16T)", 142, "AMD", "AM5"),
        new("AMD Ryzen 7 7700 (8C/16T)", 88, "AMD", "AM5"),
        new("AMD Ryzen 5 7600X (6C/12T)", 125, "AMD", "AM5"),
        new("AMD Ryzen 5 7600 / 7500F (6C/12T)", 88, "AMD", "AM5"),
        new("AMD Ryzen 7 8700G (APU Zen 4)", 88, "AMD", "AM5"),
        new("AMD Ryzen 5 8600G / 8500G (APU Zen 4)", 65, "AMD", "AM5"),

        // AMD Ryzen 5000 / 4000 (Zen 3 / Zen 2 - AM4)
        new("AMD Ryzen 9 5950X (16C/32T)", 142, "AMD", "AM4"),
        new("AMD Ryzen 9 5900X (12C/24T)", 142, "AMD", "AM4"),
        new("AMD Ryzen 7 5800X3D (8C/16T)", 120, "AMD", "AM4"),
        new("AMD Ryzen 7 5800X (8C/16T)", 142, "AMD", "AM4"),
        new("AMD Ryzen 7 5700X3D (8C/16T)", 105, "AMD", "AM4"),
        new("AMD Ryzen 7 5700X (8C/16T)", 76, "AMD", "AM4"),
        new("AMD Ryzen 7 5700 / 5700G (8C/16T)", 76, "AMD", "AM4"),
        new("AMD Ryzen 5 5600X3D (6C/12T)", 105, "AMD", "AM4"),
        new("AMD Ryzen 5 5600X (6C/12T)", 76, "AMD", "AM4"),
        new("AMD Ryzen 5 5600 / 5600T (6C/12T)", 76, "AMD", "AM4"),
        new("AMD Ryzen 5 5600G / 5500GT (6C/12T)", 65, "AMD", "AM4"),
        new("AMD Ryzen 5 5500 / 4500 (6C/12T)", 65, "AMD", "AM4"),
        new("AMD Ryzen 5 4600G (6C/12T)", 65, "AMD", "AM4"),
        new("AMD Ryzen 3 4100 / 3200G (4C/8T)", 65, "AMD", "AM4"),

        // AMD Ryzen 3000 / 2000 / 1000 (AM4)
        new("AMD Ryzen 9 3950X / 3900X (12C-16C)", 142, "AMD", "AM4"),
        new("AMD Ryzen 7 3800X / 3700X (8C/16T)", 90, "AMD", "AM4"),
        new("AMD Ryzen 5 3600X / 3600XT (6C/12T)", 95, "AMD", "AM4"),
        new("AMD Ryzen 5 3600 / 3500X (6C/12T)", 65, "AMD", "AM4"),
        new("AMD Ryzen 7 2700X / 1800X (8C/16T)", 105, "AMD", "AM4"),
        new("AMD Ryzen 7 2700 / 1700 (8C/16T)", 65, "AMD", "AM4"),
        new("AMD Ryzen 5 2600X / 1600X (6C/12T)", 95, "AMD", "AM4"),
        new("AMD Ryzen 5 2600 / 1600 AF / 1600 (6C/12T)", 65, "AMD", "AM4"),
        new("AMD Ryzen 3 3300X / 3100 / 1200 (4C/8T)", 65, "AMD", "AM4"),
        new("AMD Athlon 3000G / 200GE (2C/4T)", 35, "AMD", "AM4"),
        new("AMD Ryzen Threadripper (7000 / 3000 / 2000)", 280, "AMD", "Workstation"),

        // AMD FX, Phenom II, Athlon & APU (AM3 / AM2 / FM2)
        new("AMD FX-9590 / FX-9370 (8C/8T)", 220, "AMD", "AM3"),
        new("AMD FX-8350 / FX-8320 / FX-8300 (8C/8T)", 125, "AMD", "AM3"),
        new("AMD FX-6300 / FX-6100 (6C/6T)", 95, "AMD", "AM3"),
        new("AMD FX-4300 / FX-4100 (4C/4T)", 95, "AMD", "AM3"),
        new("AMD Phenom II X6 1100T / 1090T / 1055T (6C/6T)", 125, "AMD", "AM3"),
        new("AMD Phenom II X4 965 / 955 / 945 (4C/4T)", 125, "AMD", "AM3"),
        new("AMD Athlon II X4 645 / 640 / 620 (4C/4T)", 95, "AMD", "AM3"),
        new("AMD Athlon II X2 270 / 250 / 240 (2C/2T)", 65, "AMD", "AM3"),
        new("AMD APU A10-7850K / A10-6800K / A10-5800K (FM2+)", 95, "AMD", "AM3"),
        new("AMD APU A8-7600 / A8-6600K / A8-5600K (FM2+)", 65, "AMD", "AM3"),
        new("AMD APU A6-6400K / A4-6300 (FM2)", 65, "AMD", "AM3"),
        new("AMD Athlon 64 X2 6000+ / 5600+ / 5200+ (2C/2T)", 89, "AMD", "AM2"),
        new("AMD Athlon 64 X2 4400+ / 4200+ / 3800+ (2C/2T)", 65, "AMD", "AM2"),

        // Intel Core Ultra 200 (Arrow Lake - LGA 1851)
        new("Intel Core Ultra 9 285K (24C/24T)", 250, "Intel", "LGA1851"),
        new("Intel Core Ultra 7 265K / 265KF (20C/20T)", 250, "Intel", "LGA1851"),
        new("Intel Core Ultra 5 245K / 245KF (14C/14T)", 160, "Intel", "LGA1851"),

        // Intel 14ª Geração (Raptor Lake Refresh - LGA 1700)
        new("Intel Core i9-14900KS (24C/32T)", 350, "Intel", "LGA1700"),
        new("Intel Core i9-14900K / 14900KF (24C/32T)", 320, "Intel", "LGA1700"),
        new("Intel Core i7-14700K / 14700KF (20C/28T)", 260, "Intel", "LGA1700"),
        new("Intel Core i7-14700 / 14700F (20C/28T)", 180, "Intel", "LGA1700"),
        new("Intel Core i5-14600K / 14600KF (14C/20T)", 180, "Intel", "LGA1700"),
        new("Intel Core i5-14400 / 14400F (10C/16T)", 110, "Intel", "LGA1700"),
        new("Intel Core i3-14100 / 14100F (4C/8T)", 75, "Intel", "LGA1700"),

        // Intel 13ª Geração (Raptor Lake - LGA 1700)
        new("Intel Core i9-13900KS / 13900K / 13900KF (24C/32T)", 320, "Intel", "LGA1700"),
        new("Intel Core i7-13700K / 13700KF (16C/24T)", 253, "Intel", "LGA1700"),
        new("Intel Core i7-13700 / 13700F (16C/24T)", 180, "Intel", "LGA1700"),
        new("Intel Core i5-13600K / 13600KF (14C/20T)", 180, "Intel", "LGA1700"),
        new("Intel Core i5-13400 / 13400F (10C/16T)", 110, "Intel", "LGA1700"),
        new("Intel Core i3-13100 / 13100F (4C/8T)", 75, "Intel", "LGA1700"),

        // Intel 12ª Geração (Alder Lake - LGA 1700)
        new("Intel Core i9-12900KS / 12900K / 12900KF (16C/24T)", 241, "Intel", "LGA1700"),
        new("Intel Core i7-12700K / 12700KF (12C/20T)", 190, "Intel", "LGA1700"),
        new("Intel Core i7-12700 / 12700F (12C/20T)", 150, "Intel", "LGA1700"),
        new("Intel Core i5-12600K / 12600KF (10C/16T)", 150, "Intel", "LGA1700"),
        new("Intel Core i5-12400 / 12400F (6C/12T)", 90, "Intel", "LGA1700"),
        new("Intel Core i3-12100 / 12100F (4C/8T)", 65, "Intel", "LGA1700"),

        // Intel 11ª & 10ª Geração (LGA 1200)
        new("Intel Core i9-11900K / 11900KF (8C/16T)", 250, "Intel", "LGA1200"),
        new("Intel Core i7-11700K / 11700KF (8C/16T)", 230, "Intel", "LGA1200"),
        new("Intel Core i5-11600K / 11400 / 11400F (6C/12T)", 125, "Intel", "LGA1200"),
        new("Intel Core i9-10900K / 10850K (10C/20T)", 250, "Intel", "LGA1200"),
        new("Intel Core i7-10700K / 10700 (8C/16T)", 180, "Intel", "LGA1200"),
        new("Intel Core i5-10600K / 10400 / 10400F (6C/12T)", 100, "Intel", "LGA1200"),
        new("Intel Core i3-10100 / 10100F (4C/8T)", 65, "Intel", "LGA1200"),

        // Intel 9ª & 8ª Geração (LGA 1151)
        new("Intel Core i9-9900KS / 9900K / 9900KF (8C/16T)", 200, "Intel", "LGA1151"),
        new("Intel Core i7-9700K / 9700KF (8C/8T)", 150, "Intel", "LGA1151"),
        new("Intel Core i5-9600K / 9400F (6C/6T)", 95, "Intel", "LGA1151"),
        new("Intel Core i7-8700K / 8700 (6C/12T)", 130, "Intel", "LGA1151"),
        new("Intel Core i5-8600K / 8400 (6C/6T)", 95, "Intel", "LGA1151"),
        new("Intel Core i3-9100F / 8100 (4C/4T)", 65, "Intel", "LGA1151"),
        new("Intel Core i7-7700K / 6700K (4C/8T)", 95, "Intel", "LGA1151"),
        new("Intel Core i5-7400 / 6500 / 6400 (4C/4T)", 65, "Intel", "LGA1151"),
        new("Intel Pentium G4560 / G5400 (LGA 1151)", 54, "Intel", "LGA1151"),

        // Intel 4ª, 3ª, 2ª Geração (Haswell / Ivy / Sandy - LGA 1150 / 1155)
        new("Intel Core i7-4790K / 4770K (Haswell DDR3)", 110, "Intel", "LGA1150_1155"),
        new("Intel Core i7-4790 / 4770 (Haswell DDR3)", 84, "Intel", "LGA1150_1155"),
        new("Intel Core i5-4690K / 4590 / 4460 (Haswell DDR3)", 84, "Intel", "LGA1150_1155"),
        new("Intel Core i7-3770K / 2600K (Ivy/Sandy DDR3)", 95, "Intel", "LGA1150_1155"),
        new("Intel Core i5-3570K / 2500K / 2400 (Ivy/Sandy DDR3)", 95, "Intel", "LGA1150_1155"),
        new("Intel Core i3 (2ª a 4ª Geração DDR3)", 54, "Intel", "LGA1150_1155"),
        new("Intel Pentium G3258 / G3220 / G2030 (LGA 1150/1155)", 53, "Intel", "LGA1150_1155"),

        // Intel Xeon (Workstation & Server)
        new("Intel Xeon E5-2699 v3 / 2696 v3 (18C/36T)", 145, "Intel", "Workstation"),
        new("Intel Xeon E5-2680 v4 / 2680 v3 (12C-14C)", 120, "Intel", "Workstation"),
        new("Intel Xeon E5-2670 v3 / 2666 v3 (10C-12C)", 135, "Intel", "Workstation"),
        new("Intel Xeon E5-2640 v3 / 2630 v3 / 2620 v3 (8C-10C)", 85, "Intel", "Workstation"),
        new("Intel Xeon E5-2690 / 2670 v2 / 2689 (LGA 2011 DDR3)", 130, "Intel", "Workstation"),
        new("Intel Xeon E3-1270 / 1240 / 1230 (LGA 1150/1155)", 80, "Intel", "LGA1150_1155"),
        new("Intel Xeon X5670 / X5650 / X5660 (6C/12T - LGA 1366)", 95, "Intel", "LGA1366_1156"),
        new("Intel Xeon X5460 / E5450 / E5440 (4C/4T - Mod 771/775)", 95, "Intel", "LGA775"),

        // Intel Core Clássicos (LGA 1366 / LGA 1156)
        new("Intel Core i7-980X / 950 / 920 (Nehalem LGA 1366)", 130, "Intel", "LGA1366_1156"),
        new("Intel Core i7-870 / i5-760 / i5-750 (Lynnfield LGA 1156)", 95, "Intel", "LGA1366_1156"),
        new("Intel Core i3-540 / 530 (Clarkdale LGA 1156)", 73, "Intel", "LGA1366_1156"),

        // Intel Core 2 Quad / Core 2 Duo (Legado LGA 775)
        new("Intel Core 2 Quad Q9650 / Q9550 / Q9400 (4C/4T - LGA 775)", 95, "Intel", "LGA775"),
        new("Intel Core 2 Quad Q6600 (4C/4T - LGA 775)", 105, "Intel", "LGA775"),
        new("Intel Core 2 Duo E8400 / E8500 / E7500 (2C/2T - LGA 775)", 65, "Intel", "LGA775"),
        new("Intel Pentium Dual Core E5700 / E5400 / E2180 (LGA 775)", 65, "Intel", "LGA775"),

        // ================= NOTEBOOKS INTEL =================
        new("Intel Core i9-14900HX / 13980HX (24C/32T Laptop)", 157, "Intel Mobile", "Notebook_Intel"),
        new("Intel Core i7-14700HX / 13700HX (20C/28T Laptop)", 140, "Intel Mobile", "Notebook_Intel"),
        new("Intel Core i7-13700H / 12700H (14C/20T Laptop)", 115, "Intel Mobile", "Notebook_Intel"),
        new("Intel Core i5-13500H / 12500H (12C/16T Laptop)", 95, "Intel Mobile", "Notebook_Intel"),
        new("Intel Core Ultra 9 185H / Ultra 7 155H (Meteor Lake)", 115, "Intel Mobile", "Notebook_Intel"),
        new("Intel Core Ultra 7 258V / 256V (Lunar Lake)", 37, "Intel Mobile", "Notebook_Intel"),
        new("Intel Core Ultra 5 125H (14C/18T Laptop)", 65, "Intel Mobile", "Notebook_Intel"),
        new("Intel Core i7-11800H / i9-11900H (8C/16T Laptop)", 109, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i5-11400H / i5-11260H (6C/12T Laptop)", 75, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i7-10750H / i7-10875H (Comet Lake)", 75, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i5-10300H / i5-10200H (4C/8T Laptop)", 65, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i7-9750H / i7-8750H (6C/12T Laptop)", 75, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i5-9300H / i5-8300H (4C/8T Laptop)", 60, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i7-7700HQ / i7-6700HQ (4C/8T Laptop)", 45, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i5-7300HQ / i5-6300HQ (4C/4T Laptop)", 45, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i7-4700MQ / i7-3630QM (Haswell/Ivy Laptop)", 47, "Intel Mobile", "Notebook_Intel_Classic"),
        new("Intel Core i7-1355U / i7-1255U (Ultrafino 15W-55W)", 28, "Intel Mobile", "Notebook_Basic"),
        new("Intel Core i5-1335U / i5-1235U / i5-1135G7 (Ultrafino)", 25, "Intel Mobile", "Notebook_Basic"),
        new("Intel Core i3-1215U / i3-1115G4 / i3-1005G1", 18, "Intel Mobile", "Notebook_Basic"),
        new("Intel Celeron / Pentium N4020, N5030, N100 (Ultrafino)", 10, "Intel Mobile", "Notebook_Basic"),

        // ================= NOTEBOOKS AMD =================
        new("AMD Ryzen 9 7945HX3D / 7945HX (16C/32T Laptop)", 125, "AMD Mobile", "Notebook_AMD"),
        new("AMD Ryzen 9 7940HS / Ryzen 7 7840HS (8C/16T)", 65, "AMD Mobile", "Notebook_AMD"),
        new("AMD Ryzen 7 8845HS / 7735HS (8C/16T Laptop)", 65, "AMD Mobile", "Notebook_AMD"),
        new("AMD Ryzen 5 7640HS / 8645HS (6C/12T Laptop)", 54, "AMD Mobile", "Notebook_AMD"),
        new("AMD Ryzen 9 6900HX / Ryzen 7 6800H (8C/16T)", 65, "AMD Mobile", "Notebook_AMD"),
        new("AMD Ryzen 5 6600H (6C/12T Laptop)", 54, "AMD Mobile", "Notebook_AMD"),
        new("AMD Ryzen 9 5900HX / Ryzen 7 5800H (8C/16T)", 65, "AMD Mobile", "Notebook_AMD_Classic"),
        new("AMD Ryzen 5 5600H (6C/12T Laptop)", 54, "AMD Mobile", "Notebook_AMD_Classic"),
        new("AMD Ryzen 7 4800H (8C/16T Laptop)", 54, "AMD Mobile", "Notebook_AMD_Classic"),
        new("AMD Ryzen 5 4600H / 3550H (Laptop)", 45, "AMD Mobile", "Notebook_AMD_Classic"),
        new("AMD Ryzen 7 7730U / 5700U / 4700U (8C/16T)", 25, "AMD Mobile", "Notebook_Basic"),
        new("AMD Ryzen 5 7530U / 5500U / 4500U / 3500U (6C/12T)", 20, "AMD Mobile", "Notebook_Basic"),
        new("AMD Ryzen 3 7320U / 5300U / 3200U (Ultrafino)", 15, "AMD Mobile", "Notebook_Basic"),

        new("Outro Processador Econômico (35W - 65W)", 65, "Outro", "All")
    };

    public static readonly List<PsuComponentItem> GpuList = new()
    {
        // NVIDIA RTX 50
        new("NVIDIA GeForce RTX 5090", 600, "NVIDIA"),
        new("NVIDIA GeForce RTX 5080", 400, "NVIDIA"),
        new("NVIDIA GeForce RTX 5070 Ti", 300, "NVIDIA"),
        new("NVIDIA GeForce RTX 5070", 250, "NVIDIA"),
        new("NVIDIA GeForce RTX 5060", 140, "NVIDIA"),

        // NVIDIA RTX 40 Desktop
        new("NVIDIA GeForce RTX 4090", 450, "NVIDIA"),
        new("NVIDIA GeForce RTX 4080 / 4080 SUPER", 320, "NVIDIA"),
        new("NVIDIA GeForce RTX 4070 Ti / 4070 Ti SUPER", 285, "NVIDIA"),
        new("NVIDIA GeForce RTX 4070 / 4070 SUPER", 220, "NVIDIA"),
        new("NVIDIA GeForce RTX 4060 Ti", 165, "NVIDIA"),
        new("NVIDIA GeForce RTX 4060", 115, "NVIDIA"),

        // NVIDIA RTX 40 Laptop
        new("NVIDIA GeForce RTX 4090 Laptop GPU (150W-175W)", 175, "Notebook"),
        new("NVIDIA GeForce RTX 4080 Laptop GPU (150W-175W)", 160, "Notebook"),
        new("NVIDIA GeForce RTX 4070 Laptop GPU (100W-140W)", 130, "Notebook"),
        new("NVIDIA GeForce RTX 4060 Laptop GPU (85W-140W)", 115, "Notebook"),
        new("NVIDIA GeForce RTX 4050 Laptop GPU (60W-115W)", 90, "Notebook"),

        // NVIDIA RTX 30 Desktop
        new("NVIDIA GeForce RTX 3090 / 3090 Ti", 400, "NVIDIA"),
        new("NVIDIA GeForce RTX 3080 / 3080 Ti", 350, "NVIDIA"),
        new("NVIDIA GeForce RTX 3070 / 3070 Ti", 270, "NVIDIA"),
        new("NVIDIA GeForce RTX 3060 Ti", 200, "NVIDIA"),
        new("NVIDIA GeForce RTX 3060", 170, "NVIDIA"),
        new("NVIDIA GeForce RTX 3050", 130, "NVIDIA"),

        // NVIDIA RTX 30 Laptop
        new("NVIDIA GeForce RTX 3080 Ti / 3080 Laptop GPU", 150, "Notebook"),
        new("NVIDIA GeForce RTX 3070 Ti / 3070 Laptop GPU", 130, "Notebook"),
        new("NVIDIA GeForce RTX 3060 Laptop GPU (80W-130W)", 115, "Notebook"),
        new("NVIDIA GeForce RTX 3050 Ti / 3050 Laptop GPU (45W-80W)", 75, "Notebook"),

        // NVIDIA RTX 20 & GTX Mobile
        new("NVIDIA GeForce RTX 2080 / 2070 Laptop GPU", 130, "Notebook"),
        new("NVIDIA GeForce RTX 2060 Laptop GPU (80W-115W)", 90, "Notebook"),
        new("NVIDIA GeForce GTX 1660 Ti Mobile (60W-80W)", 80, "Notebook"),
        new("NVIDIA GeForce GTX 1650 Mobile / 1650 Ti (40W-50W)", 50, "Notebook"),
        new("NVIDIA GeForce GTX 1070 / 1060 Mobile", 110, "Notebook"),
        new("NVIDIA GeForce GTX 1050 Ti / 1050 Mobile", 65, "Notebook"),
        new("NVIDIA GeForce MX570 / MX550 / MX450 / MX350", 25, "Notebook"),

        // NVIDIA GTX Desktop (Série 16 e Série 10)
        new("NVIDIA GeForce GTX 1660 Super / Ti", 125, "NVIDIA"),
        new("NVIDIA GeForce GTX 1650 / 1650 Super", 100, "NVIDIA"),
        new("NVIDIA GeForce GTX 1080 / 1080 Ti", 250, "NVIDIA"),
        new("NVIDIA GeForce GTX 1070 / 1070 Ti", 180, "NVIDIA"),
        new("NVIDIA GeForce GTX 1060 (6GB / 3GB)", 120, "NVIDIA"),
        new("NVIDIA GeForce GTX 1050 Ti / 1050", 75, "NVIDIA"),
        new("NVIDIA GeForce GT 1030 (GDDR5/DDR4)", 30, "NVIDIA"),

        // NVIDIA Legado (Maxwell, Kepler, Fermi, Tesla)
        new("NVIDIA GeForce GTX 980 / 980 Ti", 250, "NVIDIA"),
        new("NVIDIA GeForce GTX 970", 150, "NVIDIA"),
        new("NVIDIA GeForce GTX 960", 120, "NVIDIA"),
        new("NVIDIA GeForce GTX 950", 90, "NVIDIA"),
        new("NVIDIA GeForce GTX 780 / 780 Ti / Titan", 250, "NVIDIA"),
        new("NVIDIA GeForce GTX 770 / 760", 200, "NVIDIA"),
        new("NVIDIA GeForce GTX 750 Ti / 750", 60, "NVIDIA"),
        new("NVIDIA GeForce GTX 660 / 660 Ti / 650 Ti", 130, "NVIDIA"),
        new("NVIDIA GeForce GTX 560 Ti / 550 Ti", 160, "NVIDIA"),
        new("NVIDIA GeForce GTX 460 / GTS 450", 130, "NVIDIA"),
        new("NVIDIA GeForce 9800 GT / 8800 GT", 105, "NVIDIA"),
        new("NVIDIA GeForce GT 740 / 730 / 710", 35, "NVIDIA"),
        new("NVIDIA GeForce 210 / 8400 GS", 25, "NVIDIA"),

        // AMD RX 7000 Desktop & Mobile
        new("AMD Radeon RX 7900 XTX / 7900 XT", 355, "AMD"),
        new("AMD Radeon RX 7900 GRE", 260, "AMD"),
        new("AMD Radeon RX 7800 XT", 263, "AMD"),
        new("AMD Radeon RX 7700 XT", 245, "AMD"),
        new("AMD Radeon RX 7600 XT / 7600", 180, "AMD"),
        new("AMD Radeon RX 7600M XT / 7600M (Laptop)", 120, "Notebook"),

        // AMD RX 6000 & 5000 Desktop & Mobile
        new("AMD Radeon RX 6950 XT / 6900 XT", 335, "AMD"),
        new("AMD Radeon RX 6800 XT / 6800", 300, "AMD"),
        new("AMD Radeon RX 6750 XT / 6700 XT", 250, "AMD"),
        new("AMD Radeon RX 6650 XT / 6600 XT / 6600", 160, "AMD"),
        new("AMD Radeon RX 6800M / 6700M (Laptop)", 130, "Notebook"),
        new("AMD Radeon RX 6600M / 6500M (Laptop)", 90, "Notebook"),
        new("AMD Radeon RX 5700 XT / 5700", 225, "AMD"),
        new("AMD Radeon RX 5600 XT / 5500 XT", 150, "AMD"),

        // AMD RX 500, RX 400 & R9 / R7 Legado
        new("AMD Radeon RX 590 (8GB)", 225, "AMD"),
        new("AMD Radeon RX 580 / 570 (8GB/4GB)", 185, "AMD"),
        new("AMD Radeon RX 560 / 550", 65, "AMD"),
        new("AMD Radeon RX 480 / 470", 150, "AMD"),
        new("AMD Radeon RX 460", 75, "AMD"),
        new("AMD Radeon R9 390 / 390X / 290X", 275, "AMD"),
        new("AMD Radeon R9 380 / 280X / 280", 200, "AMD"),
        new("AMD Radeon R9 270 / 270X", 160, "AMD"),
        new("AMD Radeon R7 370 / 360 / 260X", 110, "AMD"),
        new("AMD Radeon R7 250 / 240", 50, "AMD"),
        new("AMD Radeon HD 7970 / 7950 (GCN)", 220, "AMD"),
        new("AMD Radeon HD 7870 / 7850", 140, "AMD"),
        new("AMD Radeon HD 7770 / 7750", 75, "AMD"),
        new("AMD Radeon HD 6970 / 6870 / 6850", 160, "AMD"),
        new("AMD Radeon HD 6770 / 5770 / 5850", 120, "AMD"),
        new("AMD Radeon HD 6450 / 5450", 20, "AMD"),

        // Intel Arc
        new("Intel Arc A770 / A750", 225, "Intel"),
        new("Intel Arc B580", 190, "Intel"),
        new("Intel Arc A380 / A310", 75, "Intel"),

        // Vídeo integrado (Desktop e Notebook)
        new("AMD Radeon 780M / 680M (Integrada RDNA 3/2 - Notebook)", 45, "Integrado"),
        new("Intel Iris Xe Graphics (96 EUs / 80 EUs - Notebook)", 28, "Integrado"),
        new("Intel Arc Graphics 8 Cores (Core Ultra Meteor Lake)", 35, "Integrado"),
        new("Intel UHD Graphics 770 / 730 (Integrado)", 30, "Integrado"),
        new("Intel HD Graphics 530 / 4600 / 4000 / 3000 / 2000 (Integrado)", 25, "Integrado"),
        new("AMD Radeon Vega 8 / 7 / 11 (APU Integrada)", 35, "Integrado"),
        new("AMD Radeon 660M / 610M / Vega 8 Mobile (Integrada)", 25, "Integrado"),
        new("Intel UHD Graphics G1 / 620 Mobile", 15, "Integrado"),
        new("Vídeo Integrado Básico / Sem Placa Dedicada", 25, "Integrado")
    };

    /// <summary>
    /// Retorna a lista de processadores compatíveis com a placa-mãe selecionada.
    /// </summary>
    public static List<PsuComponentItem> GetCompatibleCpus(PsuComponentItem? motherboard)
    {
        if (motherboard == null) return CpuList;

        string plat = motherboard.Platform;
        if (string.IsNullOrEmpty(plat) || plat == "All" || plat == "Universal")
            return CpuList;

        if (plat == "Intermediária")
        {
            return CpuList.Where(c => c.Platform is "AM4" or "AM5" or "LGA1700" or "LGA1200" or "All").ToList();
        }

        if (plat == "Básica")
        {
            return CpuList.Where(c => c.Platform is "AM4" or "AM5" or "LGA1700" or "LGA1200" or "LGA1151" or "All").ToList();
        }

        if (plat == "Avançada" || plat == "Entusiasta")
        {
            return CpuList.Where(c => c.Platform is "AM5" or "AM4" or "LGA1700" or "LGA1851" or "All").ToList();
        }

        if (plat == "Mini-ITX")
        {
            return CpuList.Where(c => c.Platform is "AM5" or "AM4" or "LGA1700" or "LGA1200" or "All").ToList();
        }

        var direct = CpuList.Where(c => c.Platform == plat || c.Platform == "All").ToList();
        return direct.Count > 0 ? direct : CpuList;
    }

    /// <summary>
    /// Retorna as opções de memória RAM compatíveis com a placa-mãe selecionada.
    /// </summary>
    public static List<PsuRamOption> GetCompatibleRam(PsuComponentItem? motherboard)
    {
        if (motherboard == null) return AllRamOptions;

        string plat = motherboard.Platform;
        if (string.IsNullOrEmpty(plat) || plat == "All" || plat == "Universal")
            return AllRamOptions;

        return plat switch
        {
            "AM5" or "LGA1851" => AllRamOptions.Where(r => r.Generation == "DDR5").ToList(),
            "AM4" or "LGA1200" or "LGA1151" or "Notebook_AMD_Classic" => AllRamOptions.Where(r => r.Generation == "DDR4").ToList(),
            "LGA1700" or "Notebook_Intel" or "Intermediária" or "Avançada" or "Básica" or "Entusiasta" or "Mini-ITX" =>
                AllRamOptions.Where(r => r.Generation is "DDR5" or "DDR4").ToList(),
            "LGA1150_1155" or "AM3" or "Legado DDR3" or "Clássica AMD" or "LGA1366_1156" or "Legado LGA 1366/1156" =>
                AllRamOptions.Where(r => r.Generation == "DDR3").ToList(),
            "LGA775" or "Legado LGA 775" =>
                AllRamOptions.Where(r => r.Generation is "DDR3" or "DDR2").ToList(),
            "AM2" or "Legado AM2" =>
                AllRamOptions.Where(r => r.Generation == "DDR2").ToList(),
            "Workstation" =>
                AllRamOptions.Where(r => r.Generation is "DDR4" or "DDR3" or "DDR5").ToList(),
            "Notebook_AMD" =>
                AllRamOptions.Where(r => r.Generation is "DDR5" or "DDR4").ToList(),
            "Notebook_Intel_Classic" =>
                AllRamOptions.Where(r => r.Generation is "DDR4" or "DDR3").ToList(),
            "Notebook_Basic" =>
                AllRamOptions.Where(r => r.Generation is "DDR4" or "DDR5").ToList(),
            _ => AllRamOptions
        };
    }

    /// <summary>
    /// Detecta a placa-mãe atual a partir do modelo ou chipset reportado pelo sistema e retorna a melhor correspondência.
    /// </summary>
    public static PsuComponentItem DetectCurrentMotherboard(string? moboName, string? cpuName = null)
    {
        string lower = (moboName ?? "").ToLowerInvariant();
        string cpuLower = (cpuName ?? "").ToLowerInvariant();

        // 1. Detecção de Notebook se o processador ou placa for Mobile/Laptop
        bool isLaptop = lower.Contains("laptop") || lower.Contains("notebook") || lower.Contains("clevo") ||
                        lower.Contains("alienware") || lower.Contains("omen") || lower.Contains("legion") ||
                        lower.Contains("nitro") || lower.Contains("ideapad") || lower.Contains("thinkpad") ||
                        lower.Contains("latitude") || lower.Contains("inspiron") || lower.Contains("vivobook") ||
                        lower.Contains("zenbook") || lower.Contains("galaxy book") ||
                        cpuLower.Contains(" h ") || cpuLower.EndsWith("h") || cpuLower.Contains("hx") ||
                        cpuLower.Contains("hs") || cpuLower.Contains(" u ") || cpuLower.EndsWith("u") ||
                        cpuLower.Contains("ultra") || cpuLower.Contains("laptop") || cpuLower.Contains("mobile");

        if (isLaptop)
        {
            if (cpuLower.Contains("ultra") || cpuLower.Contains("14900hx") || cpuLower.Contains("13700h") || cpuLower.Contains("12700h") || cpuLower.Contains("13500h") || cpuLower.Contains("12500h") || cpuLower.Contains("12th") || cpuLower.Contains("13th") || cpuLower.Contains("14th"))
                return MotherboardList.First(m => m.Platform == "Notebook_Intel");
            if (cpuLower.Contains("7840hs") || cpuLower.Contains("8845hs") || cpuLower.Contains("6800h") || cpuLower.Contains("7940hs") || cpuLower.Contains("7945hx"))
                return MotherboardList.First(m => m.Platform == "Notebook_AMD");
            if (cpuLower.Contains("5800h") || cpuLower.Contains("5600h") || cpuLower.Contains("4800h") || cpuLower.Contains("4600h"))
                return MotherboardList.First(m => m.Platform == "Notebook_AMD_Classic");
            if (cpuLower.Contains("11800h") || cpuLower.Contains("10750h") || cpuLower.Contains("9750h") || cpuLower.Contains("8750h") || cpuLower.Contains("7700hq") || cpuLower.Contains("4700mq"))
                return MotherboardList.First(m => m.Platform == "Notebook_Intel_Classic");
            if (cpuLower.Contains(" u") || cpuLower.Contains("celeron") || cpuLower.Contains("pentium n") || cpuLower.Contains("n100"))
                return MotherboardList.First(m => m.Platform == "Notebook_Basic");

            return MotherboardList.First(m => m.Category == "Notebook");
        }

        // 2. Detecções Desktop
        if (lower.Contains("x99") || lower.Contains("x299") || lower.Contains("trx40") || lower.Contains("wrx80") || lower.Contains("c602") || lower.Contains("c612") || lower.Contains("server"))
            return MotherboardList.First(m => m.Category == "Workstation");

        if (lower.Contains("maximus") || lower.Contains("crosshair") || lower.Contains("godlike") || lower.Contains("extreme") || lower.Contains("apex"))
            return MotherboardList.First(m => m.Category == "Entusiasta");

        if (lower.Contains("x870") || lower.Contains("x670") || lower.Contains("x570") || lower.Contains("x470") || lower.Contains("x370") ||
            lower.Contains("z890") || lower.Contains("z790") || lower.Contains("z690") || lower.Contains("z590") || lower.Contains("z490") || lower.Contains("z390") || lower.Contains("z270") || lower.Contains("z170") || lower.Contains("z97") || lower.Contains("z87") || lower.Contains("z77"))
            return MotherboardList.First(m => m.Category == "Avançada");

        if (lower.Contains("g31") || lower.Contains("g41") || lower.Contains("p35") || lower.Contains("p45") || lower.Contains("775") || lower.Contains("lga775"))
            return MotherboardList.First(m => m.Category == "Legado LGA 775");

        if (lower.Contains("h55") || lower.Contains("p55") || lower.Contains("x58") || lower.Contains("1156") || lower.Contains("1366"))
            return MotherboardList.First(m => m.Category == "Legado LGA 1366/1156");

        if (lower.Contains("970") || lower.Contains("990fx") || lower.Contains("am3") || lower.Contains("760g") || lower.Contains("a68h") || lower.Contains("a88x") || lower.Contains("fm2"))
            return MotherboardList.First(m => m.Category == "Clássica AMD");

        if (lower.Contains("am2") || lower.Contains("780g") || lower.Contains("790gx") || lower.Contains("nforce"))
            return MotherboardList.First(m => m.Category == "Legado AM2");

        if (lower.Contains("h61") || lower.Contains("h81") || lower.Contains("b75") || lower.Contains("b85") || lower.Contains("z77") || lower.Contains("z97") || lower.Contains("1155") || lower.Contains("1150"))
            return MotherboardList.First(m => m.Category == "Legado DDR3");

        if (lower.Contains("a320") || lower.Contains("a520") || lower.Contains("a620") || lower.Contains("h310") || lower.Contains("h410") || lower.Contains("h510") || lower.Contains("h610") || lower.Contains("h710"))
            return MotherboardList.First(m => m.Category == "Básica");

        if (lower.Contains("itx"))
            return MotherboardList.First(m => m.Category == "Mini-ITX");

        return MotherboardList[0];
    }

    /// <summary>
    /// Detecta o processador atual a partir do nome reportado pelo sistema e retorna a melhor correspondência.
    /// </summary>
    public static PsuComponentItem DetectCurrentCpu(string? cpuName)
    {
        if (string.IsNullOrWhiteSpace(cpuName))
            return CpuList.First(c => c.Name.Contains("5600"));

        foreach (var item in CpuList)
        {
            var parts = item.Name.Split('(')[0].Split('/');
            foreach (var p in parts)
            {
                var clean = p.Trim();
                if (clean.StartsWith("AMD ", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(4).Trim();
                else if (clean.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(6).Trim();

                if (clean.Length >= 4 && cpuName.Contains(clean, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }

        // Tenta palavras-chave de alta prioridade (incluindo Mobile)
        string[] highPriority = {
            // Notebook Mobile
            "14900HX", "13980HX", "14700HX", "13700HX", "13700H", "12700H", "13500H", "12500H",
            "11800H", "11400H", "10750H", "10300H", "9750H", "8750H", "7700HQ", "4700MQ",
            "185H", "155H", "125H", "258V", "256V", "1355U", "1255U", "1335U", "1235U", "1135G7",
            "7945HX", "7940HS", "7840HS", "8845HS", "7735HS", "7640HS", "6900HX", "6800H", "6600H",
            "5900HX", "5800H", "5600H", "4800H", "4600H", "5700U", "5500U", "4500U", "3500U",
            // Desktop
            "9950X", "9900X", "9800X3D", "9700X", "9600X",
            "7950X3D", "7950X", "7900X3D", "7900X", "7800X3D", "7700X", "7700", "7600X", "7600", "7500F",
            "5950X", "5900X", "5800X3D", "5800X", "5700X3D", "5700X", "5700G", "5600X3D", "5600X", "5600G", "5600", "5500", "4500", "4600G",
            "3950X", "3900X", "3800X", "3700X", "3600X", "3600", "2700X", "2600X", "2600", "1600",
            "14900KS", "14900K", "14700K", "14600K", "14400", "14100",
            "13900KS", "13900K", "13700K", "13600K", "13400", "13100",
            "12900KS", "12900K", "12700K", "12600K", "12400", "12100",
            "11900K", "11700K", "11600K", "11400",
            "10900K", "10700K", "10400", "10100",
            "9900K", "9700K", "9600K", "9400F", "8700K", "8400",
            "7700K", "6700K", "4790K", "4770K", "3770K", "2600K", "2500K",
            "285K", "265K", "245K",
            "2696 v3", "2680 v4", "2670 v3", "2666 v3", "2650 v3", "2620 v3", "2689", "X5670", "X5650", "X5460", "E5450",
            "FX-9590", "FX-8350", "FX-6300", "FX-4300",
            "1100T", "1090T", "1055T", "Phenom", "Athlon", "A10-7850K", "A8-7600", "A6-6400K",
            "Q9650", "Q9550", "Q9400", "Q6600", "E8500", "E8400", "E7500", "E5700", "E5400",
            "G4560", "G3258", "G3220", "G2030"
        };

        foreach (var kw in highPriority)
        {
            if (cpuName.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                // Garante que o modelo não case como prefixo de outro (ex: "13700H" não deve casar com "13700HX")
                var match = CpuList.FirstOrDefault(c =>
                {
                    int idx = c.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) return false;
                    int nextIdx = idx + kw.Length;
                    if (nextIdx < c.Name.Length && char.IsLetterOrDigit(c.Name[nextIdx])) return false;
                    return true;
                });
                if (match != null) return match;
            }
        }

        if (cpuName.Contains("Core 2 Quad", StringComparison.OrdinalIgnoreCase) || cpuName.Contains("Core(TM)2 Quad", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("Q6600") || c.Name.Contains("Q9650"));
        if (cpuName.Contains("Core 2 Duo", StringComparison.OrdinalIgnoreCase) || cpuName.Contains("Core(TM)2 Duo", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("E8400"));
        if (cpuName.Contains("Phenom", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("Phenom II X4"));
        if (cpuName.Contains("Athlon 64", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("Athlon 64 X2"));
        if (cpuName.Contains("Athlon II", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("Athlon II X4"));
        if (cpuName.Contains("i9", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("14900K"));
        if (cpuName.Contains("i7", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("14700K"));
        if (cpuName.Contains("i5", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("13400") || c.Name.Contains("12400"));
        if (cpuName.Contains("i3", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("12100"));
        if (cpuName.Contains("Ryzen 9", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("7950X") || c.Name.Contains("9950X"));
        if (cpuName.Contains("Ryzen 7", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("7800X3D"));
        if (cpuName.Contains("Ryzen 5", StringComparison.OrdinalIgnoreCase))
            return CpuList.First(c => c.Name.Contains("5600"));

        return CpuList.Last();
    }

    /// <summary>
    /// Detecta a placa de vídeo atual e retorna a melhor correspondência do catálogo.
    /// </summary>
    public static PsuComponentItem DetectCurrentGpu(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
            return GpuList.First(g => g.Name.Contains("RTX 4060"));

        bool isMobileQuery = gpuName.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
                             gpuName.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
                             gpuName.Contains("Max-Q", StringComparison.OrdinalIgnoreCase);

        foreach (var item in GpuList)
        {
            if (isMobileQuery && item.Category != "Notebook" && item.Category != "Integrado")
                continue; // Dá preferência absoluta para variantes de notebook quando a GPU for laptop
            if (!isMobileQuery && item.Category == "Notebook")
                continue;

            var parts = item.Name.Split('/');
            foreach (var p in parts)
            {
                var clean = p.Trim();
                if (clean.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase))
                    clean = clean.Substring(7).Trim();
                else if (clean.StartsWith("AMD ", StringComparison.OrdinalIgnoreCase))
                    clean = clean.Substring(4).Trim();

                if (clean.Length > 4 && (gpuName.Contains(clean, StringComparison.OrdinalIgnoreCase) || clean.Contains(gpuName, StringComparison.OrdinalIgnoreCase)))
                    return item;
            }
        }

        // Busca por subcadeias comuns (incluindo Mobile/Laptop)
        string[] keywords = {
            "4090 Laptop", "4080 Laptop", "4070 Laptop", "4060 Laptop", "4050 Laptop",
            "3080 Laptop", "3070 Laptop", "3060 Laptop", "3050 Laptop", "3050 Ti Laptop",
            "2080 Laptop", "2070 Laptop", "2060 Laptop", "1660 Ti Mobile", "1650 Mobile", "MX550", "MX450",
            "7600M", "6800M", "6700M", "6600M", "780M", "680M", "Iris Xe", "Arc A370M",
            "5090", "5080", "5070", "5060",
            "4090", "4080", "4070", "4060",
            "3090", "3080", "3070", "3060", "3050",
            "1660", "1650",
            "1080", "1070", "1060", "1050", "1030",
            "980", "970", "960", "950",
            "780", "770", "760", "750",
            "660", "650", "560", "550", "460", "450", "9800", "8800", "730", "710", "210",
            "7900", "7800", "7700", "7600",
            "6900", "6800", "6700", "6600",
            "5700", "5600", "5500",
            "590", "580", "570", "560", "550", "480", "470", "460",
            "R9 390", "R9 380", "R9 280", "R9 270", "R7 370", "R7 260", "R7 250", "R7 240",
            "HD 7970", "HD 7870", "HD 7850", "HD 7770", "HD 6870", "HD 6850", "HD 6770", "HD 5770", "HD 6450", "HD 5450",
            "A770", "B580", "A380"
        };
        foreach (var kw in keywords)
        {
            if (gpuName.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                var match = GpuList.FirstOrDefault(g => g.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
        }

        // Se for gráfico integrado
        if (gpuName.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Intel UHD", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Iris", StringComparison.OrdinalIgnoreCase))
        {
            return GpuList.First(g => g.Category == "Integrado");
        }

        return GpuList.First(g => g.Name.Contains("RTX 4060"));
    }

    /// <summary>
    /// Calcula o consumo detalhado e gera recomendação de fonte e orientações de upgrade.
    /// </summary>
    public static PsuCalculationResult Calculate(PsuConfig config)
    {
        int cpuWatts = config.CpuWatts;
        int gpuWatts = config.GpuWatts;

        // Placa-mãe
        int moboWatts = config.MotherboardWatts > 0 ? config.MotherboardWatts : 50;

        // Memória RAM
        int ramWatts;
        if (config.RamWattsOverride > 0)
        {
            ramWatts = config.RamWattsOverride;
        }
        else if (config.RamType.Contains("DDR2", StringComparison.OrdinalIgnoreCase))
        {
            ramWatts = config.RamSticks * 2;
        }
        else if (config.RamType.Contains("DDR3", StringComparison.OrdinalIgnoreCase))
        {
            ramWatts = config.RamSticks * 3;
        }
        else if (config.RamType.Contains("DDR5", StringComparison.OrdinalIgnoreCase))
        {
            ramWatts = config.RamSticks * 6;
        }
        else
        {
            ramWatts = config.RamSticks * 4;
        }

        // Armazenamento
        int storageWatts = (config.NvmeCount * 8) + (config.SataSsdCount * 4) + (config.HddCount * 12);
        if (storageWatts == 0) storageWatts = 8; // no mínimo 1 drive

        // Refrigeração & Ventoinhas
        int coolingWatts;
        if (config.CoolerType.Contains("360"))
            coolingWatts = 30; // bomba + 3 fans
        else if (config.CoolerType.Contains("240"))
            coolingWatts = 22; // bomba + 2 fans
        else if (config.CoolerType.Contains("Duplo"))
            coolingWatts = 15;
        else
            coolingWatts = 8;  // air cooler fan padrão

        coolingWatts += Math.Max(0, config.FanCount) * 3; // ventoinhas adicionais

        // Periféricos e USB
        int usbWatts = config.RgbAndPeripherals ? 20 : 10;

        // Total base
        int baseTotal = cpuWatts + gpuWatts + moboWatts + ramWatts + storageWatts + coolingWatts + usbWatts;

        // Overclock adicional (+15%)
        if (config.Overclock)
        {
            int ocExtra = (int)Math.Round((cpuWatts + gpuWatts) * 0.15);
            cpuWatts += (int)Math.Round(cpuWatts * 0.15);
            gpuWatts += (int)Math.Round(gpuWatts * 0.15);
            baseTotal += ocExtra;
        }

        int totalPeak = baseTotal;

        // Identifica se é configuração de Notebook
        bool isNotebook = config.MotherboardName.Contains("Notebook", StringComparison.OrdinalIgnoreCase) ||
                          config.CpuName.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
                          config.CpuName.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
                          config.GpuName.Contains("Laptop", StringComparison.OrdinalIgnoreCase);

        int recommendedPsu;
        string efficiency;
        string pcie;
        string advice;

        if (isNotebook)
        {
            double targetWatts = totalPeak * 1.25;
            recommendedPsu = RoundToStandardNotebookCharger((int)Math.Ceiling(targetWatts));
            efficiency = "Fonte Externa / Carregador Original (Bivolt)";
            pcie = "Alimentação interna integrada da placa-mãe (BGA/MXM)";
            advice = $"Setup de Notebook / Portátil: O consumo máximo estimado em carga pesada é de {totalPeak}W. Um carregador original de {recommendedPsu}W (ou USB-C PD 100W/140W/240W EPR compatível) garante desempenho máximo com folga térmica e sem descarregar a bateria durante jogos.";
        }
        else
        {
            double targetWatts = totalPeak * 1.30;
            recommendedPsu = RoundToStandardPsu((int)Math.Ceiling(targetWatts));
            efficiency = recommendedPsu switch
            {
                >= 850 => "80 PLUS GOLD / PLATINUM (ATX 3.0)",
                >= 650 => "80 PLUS GOLD / BRONZE",
                _ => "80 PLUS BRONZE"
            };
            pcie = gpuWatts switch
            {
                >= 400 => "Cabo nativo 12V-2x6 (12VHPWR 600W) PCIe 5.0 ou 3x PCIe 8-pin dedicados",
                >= 280 => "Cabo 12VHPWR / 12V-2x6 ou 2x PCIe 8-pin individuais (não usar cabo split)",
                >= 180 => "2x conectores PCIe 8-pin dedicados",
                >= 100 => "1x conector PCIe 8-pin / 6-pin",
                _ => "Sem necessidade de cabo PCIe extra (alimentação pelo slot)"
            };
            advice = GenerateUpgradeAdvice(totalPeak, recommendedPsu, gpuWatts, config.Overclock);
        }

        int headroom = recommendedPsu - totalPeak;
        double loadPct = (double)totalPeak / recommendedPsu * 100.0;

        return new PsuCalculationResult(
            CpuWatts: cpuWatts,
            GpuWatts: gpuWatts,
            MotherboardWatts: moboWatts,
            RamWatts: ramWatts,
            StorageWatts: storageWatts,
            CoolingWatts: coolingWatts,
            UsbAndMiscWatts: usbWatts,
            TotalPeakWatts: totalPeak,
            RecommendedPsuWatts: recommendedPsu,
            RecommendedEfficiency: efficiency,
            PcieCablesRecommendation: pcie,
            UpgradeAdvice: advice,
            HeadroomWatts: headroom,
            LoadPercentageAtRecommended: loadPct
        );
    }

    private static int RoundToStandardPsu(int watts)
    {
        int[] standards = { 450, 500, 550, 600, 650, 700, 750, 850, 1000, 1200, 1500 };
        foreach (var s in standards)
        {
            if (s >= watts) return s;
        }
        return 1600;
    }

    private static int RoundToStandardNotebookCharger(int watts)
    {
        int[] standards = { 65, 90, 120, 135, 150, 180, 230, 240, 280, 300, 330 };
        foreach (var s in standards)
        {
            if (s >= watts) return s;
        }
        return 330;
    }

    private static string GenerateUpgradeAdvice(int totalPeak, int recommendedPsu, int gpuWatts, bool overclock)
    {
        if (recommendedPsu <= 500)
        {
            return "Consumo leve: Uma fonte padrão de 500W ou 550W suporta tranquilamente essa máquina, operando na faixa de máxima eficiência com baixo ruído.";
        }
        if (recommendedPsu <= 650)
        {
            return "Configuração intermediária para jogos: Uma fonte de 600W a 650W é o ponto ideal. Se for comprar uma nova, prefira modelos 80 Plus Gold com cabos independentes.";
        }
        if (recommendedPsu <= 750)
        {
            return "Configuração entusiasta: Recomendada fonte de 700W a 750W 80 Plus Gold. Placas de vídeo desse porte geram transientes de microsegundos que desligam fontes inferiores.";
        }
        if (recommendedPsu <= 850)
        {
            return "Hardware de alto desempenho: Uma fonte de 850W é fortemente recomendada. Prefira padrão ATX 3.0 / PCIe 5.0 com conector nativo 12V-2x6 para evitar adaptadores.";
        }
        return "Configuração extrema (topo de linha): Recomendada fonte de 1000W a 1200W ATX 3.0 para garantir estabilidade absoluta e headroom durante picos extremos de carga combinada.";
    }
}
