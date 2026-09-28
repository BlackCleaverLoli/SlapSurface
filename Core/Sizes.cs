namespace SlapSurface;

internal enum ButtonSizeKind { Auto = 0, Square, Rect }

internal readonly record struct ButtonSize
{
    public ButtonSizeKind Kind { get; }
    public float WidthUnits { get; }
    public float HeightUnits { get; }

    private ButtonSize(ButtonSizeKind kind, float widthUnits, float heightUnits)
    {
        Kind = kind;
        WidthUnits = widthUnits;
        HeightUnits = heightUnits;
    }

    public static ButtonSize Auto => new(ButtonSizeKind.Auto, 0f, 0f);
    public static ButtonSize Square(float units = 1f) =>
        new(ButtonSizeKind.Square, Normalize(units), Normalize(units));
    public static ButtonSize Rect(float widthUnits, float heightUnits) =>
        new(ButtonSizeKind.Rect, Normalize(widthUnits), Normalize(heightUnits));

    private static float Normalize(float value) => value > 0f && !float.IsInfinity(value) ? value : 1f;
}

internal enum PanelSizeKind { FillAvailable = 0, FillWidth }

internal readonly record struct PanelSize
{
    public PanelSizeKind Kind { get; }
    public float HeightUnits { get; }

    private PanelSize(PanelSizeKind kind, float heightUnits)
    {
        Kind = kind;
        HeightUnits = heightUnits;
    }

    public static PanelSize FillAvailable => new(PanelSizeKind.FillAvailable, 0f);
    public static PanelSize FillWidth(float heightUnits = 1f) =>
        new(PanelSizeKind.FillWidth, Normalize(heightUnits));

    private static float Normalize(float value) => value > 0f && !float.IsInfinity(value) ? value : 1f;
}

internal enum SurfaceSizeKind { Auto = 0, FillWidth, FillAvailable }

internal readonly record struct SurfaceSize
{
    public SurfaceSizeKind Kind { get; }
    public float HeightUnits { get; }

    private SurfaceSize(SurfaceSizeKind kind, float heightUnits)
    {
        Kind = kind;
        HeightUnits = heightUnits;
    }

    public static SurfaceSize Auto => new(SurfaceSizeKind.Auto, 1f);
    public static SurfaceSize FillWidth(float heightUnits = 1f) =>
        new(SurfaceSizeKind.FillWidth, Normalize(heightUnits));
    public static SurfaceSize FillAvailable => new(SurfaceSizeKind.FillAvailable, 0f);

    private static float Normalize(float value) => value > 0f && !float.IsInfinity(value) ? value : 1f;
}
