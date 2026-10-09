using SkiaSharp;

namespace AppOrbit.Scene;

/// <summary>
/// A 4x4 matrix in the CSS transform convention: column vectors, y down, z toward the viewer.
/// The web prototype places cards with CSS 3D transforms; this reproduces the same pipeline
/// so the Uno scene lands every card where the prototype does.
/// </summary>
public readonly struct Mat4
{
    private readonly double[] _m; // row-major, 16

    private Mat4(double[] m) { _m = m; }

    public static Mat4 Identity => new(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 });

    public double this[int row, int col] => _m[row * 4 + col];

    public static Mat4 Translate(double x, double y, double z) =>
        new(new double[] { 1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1 });

    public static Mat4 Scale(double s) =>
        new(new double[] { s, 0, 0, 0, 0, s, 0, 0, 0, 0, s, 0, 0, 0, 0, 1 });

    /// <summary>CSS rotateX(deg).</summary>
    public static Mat4 RotateX(double deg)
    {
        var (s, c) = Math.SinCos(deg * Math.PI / 180);
        return new(new double[] { 1, 0, 0, 0, 0, c, -s, 0, 0, s, c, 0, 0, 0, 0, 1 });
    }

    /// <summary>CSS rotateY(deg).</summary>
    public static Mat4 RotateY(double deg)
    {
        var (s, c) = Math.SinCos(deg * Math.PI / 180);
        return new(new double[] { c, 0, s, 0, 0, 1, 0, 0, -s, 0, c, 0, 0, 0, 0, 1 });
    }

    /// <summary>CSS perspective(d): w' = w - z / d.</summary>
    public static Mat4 Perspective(double d) =>
        new(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, -1 / d, 1 });

    public static Mat4 operator *(Mat4 a, Mat4 b)
    {
        var r = new double[16];
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
            {
                double s = 0;
                for (var k = 0; k < 4; k++) s += a._m[i * 4 + k] * b._m[k * 4 + j];
                r[i * 4 + j] = s;
            }
        return new Mat4(r);
    }

    /// <summary>Projects a point on this matrix's plane: screen x, y and the depth before perspective.</summary>
    public (double X, double Y, double Depth) Project(double x, double y, double z = 0)
    {
        var px = _m[0] * x + _m[1] * y + _m[2] * z + _m[3];
        var py = _m[4] * x + _m[5] * y + _m[6] * z + _m[7];
        var pz = _m[8] * x + _m[9] * y + _m[10] * z + _m[11];
        var pw = _m[12] * x + _m[13] * y + _m[14] * z + _m[15];
        if (Math.Abs(pw) < 1e-9) pw = 1e-9;
        return (px / pw, py / pw, pz);
    }

    /// <summary>The plane z = 0 of this matrix as a Skia perspective matrix (local x, y → screen).</summary>
    public SKMatrix ToPlaneMatrix() => new(
        (float)_m[0], (float)_m[1], (float)_m[3],
        (float)_m[4], (float)_m[5], (float)_m[7],
        (float)_m[12], (float)_m[13], (float)_m[15]);
}

/// <summary>scene.js camera: yaw, pitch and a continuous scale, eased toward a target, clamped; a fit scale and centre from the layout bounds.</summary>
public sealed class Camera
{
    public const double YawMax = 40, PitchMax = 22, ScaleMin = 0.45, ScaleMax = 2.4, PerspectiveD = 1400;

    public double Yaw = -18, Pitch = 6, Scale = 1;
    public double TargetYaw = -18, TargetPitch = 6, TargetScale = 1;
    public double FitScale = 1, Bx, By;

    public static (double Yaw, double Pitch) Default(string view) => view == "flat" ? (0, 0) : (-18, 6);

    public double S => FitScale * Scale;

    public void SetTarget(double? yaw = null, double? pitch = null, double? scale = null)
    {
        if (yaw != null) TargetYaw = Math.Clamp(yaw.Value, -YawMax, YawMax);
        if (pitch != null) TargetPitch = Math.Clamp(pitch.Value, -PitchMax, PitchMax);
        if (scale != null) TargetScale = Math.Clamp(scale.Value, ScaleMin, ScaleMax);
    }

    public void Snap() { Yaw = TargetYaw; Pitch = TargetPitch; Scale = TargetScale; }

    /// <summary>One easing step (0.18 of the remaining distance, as the prototype). Returns true while still moving.</summary>
    public bool Step()
    {
        var moving = false;
        Ease(ref Yaw, TargetYaw, 0.05, ref moving);
        Ease(ref Pitch, TargetPitch, 0.05, ref moving);
        Ease(ref Scale, TargetScale, 0.002, ref moving);
        return moving;
    }

    private static void Ease(ref double v, double t, double eps, ref bool moving)
    {
        var d = t - v;
        if (Math.Abs(d) > eps) { v += d * 0.18; moving = true; } else v = t;
    }

    public void Fit(double width, double height, Layout.Bounds b, bool docked)
    {
        var padX = docked ? 24 : 90;
        var padY = docked ? 40 : 110;
        var s = Math.Min(Math.Min((width - padX) / Math.Max(b.W, 1), (height - padY) / Math.Max(b.H, 1)), 1);
        FitScale = Math.Max(s, 0.25);
        Bx = b.X; By = b.Y;
    }

    /// <summary>The world matrix for a stage of the given size, as the prototype's .stage perspective + .world transform.</summary>
    public Mat4 World(double width, double height) =>
        Mat4.Translate(width / 2, height / 2, 0) * Mat4.Perspective(PerspectiveD) * Mat4.Scale(S)
        * Mat4.RotateX(Pitch) * Mat4.RotateY(Yaw) * Mat4.Translate(-Bx, -By, 0);
}
