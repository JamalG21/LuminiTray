using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace LuminiTray;

/// <summary>
/// Nó geométrico calculado pelo algoritmo Squarified Treemap para renderização 2D responsiva.
/// </summary>
public sealed class TreemapTile
{
    public DiskSpaceItem Item { get; set; } = null!;
    public Rect Bounds { get; set; }
    public Color CategoryColor { get; set; }
    public SolidColorBrush FillBrush { get; set; } = null!;
    public SolidColorBrush BorderBrush { get; set; } = null!;
    public bool IsHeaderOnly { get; set; }
    public List<TreemapTile> Children { get; set; } = new();
}

/// <summary>
/// Implementação do algoritmo Squarified Treemap (Bruls, Huizing, van Wijk)
/// para particionamento 2D hierárquico idêntico ao modelo visual do Mapa do Disco (HUB Pro / WinDirStat).
/// </summary>
public static class TreemapLayoutEngine
{
    public static readonly Dictionary<string, Color> CategoryColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Vídeos", Color.FromRgb(0xE1, 0x1D, 0x48) },             // #E11D48 (Rose)
        { "Imagens", Color.FromRgb(0x06, 0xB6, 0xD4) },            // #06B6D4 (Cyan)
        { "Músicas", Color.FromRgb(0xA8, 0x55, 0xF7) },            // #A855F7 (Purple)
        { "Documentos", Color.FromRgb(0xF5, 0x9E, 0x0B) },         // #F59E0B (Amber)
        { "Programas e jogos", Color.FromRgb(0x10, 0xB9, 0x81) },  // #10B981 (Emerald)
        { "Compactados", Color.FromRgb(0xF9, 0x73, 0x16) },        // #F97316 (Orange)
        { "Windows e sistema", Color.FromRgb(0x3B, 0x82, 0xF6) },  // #3B82F6 (Blue)
        { "Outros", Color.FromRgb(0x64, 0x74, 0x8B) }              // #64748B (Slate)
    };

    public static Color GetCategoryColor(string category)
    {
        if (CategoryColors.TryGetValue(category, out var color))
        {
            return color;
        }
        return CategoryColors["Outros"];
    }

    /// <summary>
    /// Calcula a árvore hierárquica de retângulos Squarified para uma pasta e suas subpastas.
    /// </summary>
    public static List<TreemapTile> CalculateLayout(DiskSpaceItem rootFolder, Rect canvasBounds, int maxDepth = 2)
    {
        var tiles = new List<TreemapTile>();
        if (rootFolder == null || rootFolder.Children.Count == 0 || canvasBounds.Width <= 10 || canvasBounds.Height <= 10)
        {
            return tiles;
        }

        var validItems = rootFolder.Children
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.Size)
            .ToList();

        if (validItems.Count == 0) return tiles;

        Squarify(validItems, canvasBounds, tiles, depth: 0, maxDepth: maxDepth);
        return tiles;
    }

    private static void Squarify(List<DiskSpaceItem> items, Rect rect, List<TreemapTile> output, int depth, int maxDepth)
    {
        if (items.Count == 0 || rect.Width <= 4 || rect.Height <= 4) return;

        double totalSize = items.Sum(i => (double)i.Size);
        if (totalSize <= 0) return;

        double totalArea = rect.Width * rect.Height;
        var areas = items.Select(i => (i, (double)i.Size / totalSize * totalArea)).ToList();

        SquarifyCore(areas, new List<(DiskSpaceItem Item, double Area)>(), rect, output, depth, maxDepth);
    }

    private static void SquarifyCore(
        List<(DiskSpaceItem Item, double Area)> remaining,
        List<(DiskSpaceItem Item, double Area)> row,
        Rect rect,
        List<TreemapTile> output,
        int depth,
        int maxDepth)
    {
        if (remaining.Count == 0)
        {
            LayoutRow(row, rect, output, depth, maxDepth);
            return;
        }

        if (rect.Width <= 2 || rect.Height <= 2) return;

        double shortestSide = Math.Min(rect.Width, rect.Height);
        var next = remaining[0];

        if (row.Count == 0)
        {
            row.Add(next);
            SquarifyCore(remaining.Skip(1).ToList(), row, rect, output, depth, maxDepth);
            return;
        }

        double currentWorst = WorstAspect(row, shortestSide);
        var candidateRow = new List<(DiskSpaceItem Item, double Area)>(row) { next };
        double candidateWorst = WorstAspect(candidateRow, shortestSide);

        if (candidateWorst <= currentWorst)
        {
            // Adicionar melhora ou mantém a proporção
            SquarifyCore(remaining.Skip(1).ToList(), candidateRow, rect, output, depth, maxDepth);
        }
        else
        {
            // Fecha a linha atual e inicia nova linha no espaço restante
            Rect remainingRect = LayoutRow(row, rect, output, depth, maxDepth);
            SquarifyCore(remaining, new List<(DiskSpaceItem Item, double Area)>(), remainingRect, output, depth, maxDepth);
        }
    }

    private static Rect LayoutRow(
        List<(DiskSpaceItem Item, double Area)> row,
        Rect rect,
        List<TreemapTile> output,
        int depth,
        int maxDepth)
    {
        if (row.Count == 0) return rect;

        double rowArea = row.Sum(r => r.Area);
        bool isHorizontal = rect.Width >= rect.Height;

        double rowThickness = isHorizontal ? rowArea / rect.Height : rowArea / rect.Width;
        if (double.IsNaN(rowThickness) || rowThickness <= 0) return rect;

        double currentPos = isHorizontal ? rect.Y : rect.X;

        foreach (var (item, area) in row)
        {
            double itemLength = area / rowThickness;
            if (double.IsNaN(itemLength) || itemLength <= 0) continue;

            Rect tileBounds = isHorizontal
                ? new Rect(rect.X, currentPos, Math.Min(rowThickness, rect.Width), Math.Min(itemLength, rect.Height - (currentPos - rect.Y)))
                : new Rect(currentPos, rect.Y, Math.Min(itemLength, rect.Width - (currentPos - rect.X)), Math.Min(rowThickness, rect.Height));

            currentPos += itemLength;

            if (tileBounds.Width <= 1 || tileBounds.Height <= 1) continue;

            var baseColor = GetCategoryColor(item.Category);
            // Variação suave de saturação e brilho por nível de profundidade
            byte alpha = (byte)Math.Clamp(230 - (depth * 35), 140, 255);
            var fillBrush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            var borderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
            fillBrush.Freeze();
            borderBrush.Freeze();

            var tile = new TreemapTile
            {
                Item = item,
                Bounds = tileBounds,
                CategoryColor = baseColor,
                FillBrush = fillBrush,
                BorderBrush = borderBrush
            };

            // Recursão aninhada para subpastas com espaço visual suficiente
            if (item.IsDirectory && depth < maxDepth && tileBounds.Width >= 80 && tileBounds.Height >= 55)
            {
                var children = item.Children
                    .Where(c => c.Size > 0)
                    .OrderByDescending(c => c.Size)
                    .Take(12)
                    .ToList();

                if (children.Count > 0)
                {
                    double headerHeight = 22.0;
                    Rect innerBounds = new Rect(
                        tileBounds.X + 3,
                        tileBounds.Y + headerHeight,
                        Math.Max(0, tileBounds.Width - 6),
                        Math.Max(0, tileBounds.Height - headerHeight - 3));

                    if (innerBounds.Width > 20 && innerBounds.Height > 20)
                    {
                        var childTiles = new List<TreemapTile>();
                        Squarify(children, innerBounds, childTiles, depth + 1, maxDepth);
                        tile.Children = childTiles;
                    }
                }
            }

            output.Add(tile);
        }

        // Subtrai a espessura da linha processada da área restante
        if (isHorizontal)
        {
            double newWidth = Math.Max(0, rect.Width - rowThickness);
            return new Rect(rect.X + rowThickness, rect.Y, newWidth, rect.Height);
        }
        else
        {
            double newHeight = Math.Max(0, rect.Height - rowThickness);
            return new Rect(rect.X, rect.Y + rowThickness, rect.Width, newHeight);
        }
    }

    private static double WorstAspect(List<(DiskSpaceItem Item, double Area)> row, double sideLength)
    {
        if (row.Count == 0 || sideLength <= 0) return double.MaxValue;

        double sumArea = row.Sum(r => r.Area);
        if (sumArea <= 0) return double.MaxValue;

        double sideSquared = sideLength * sideLength;
        double minArea = row.Min(r => r.Area);
        double maxArea = row.Max(r => r.Area);

        double aspect1 = (sideSquared * maxArea) / (sumArea * sumArea);
        double aspect2 = (sumArea * sumArea) / (sideSquared * minArea);

        return Math.Max(aspect1, aspect2);
    }
}
