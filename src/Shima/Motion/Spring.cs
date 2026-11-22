// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Motion;

/// <summary>一條彈簧：數值朝目標前進，帶一點回彈。島的寬與高各用一條。</summary>
public sealed class Spring(double value, double stiffness = 330, double damping = 27)
{
    public double Value { get; private set; } = value;
    public double Velocity { get; private set; }
    public double Target { get; set; } = value;

    public bool Settled => Math.Abs(Target - Value) < 0.25 && Math.Abs(Velocity) < 2;

    public void Step(double seconds)
    {
        // 切成小步積分，畫面卡頓時彈簧也不會爆開
        int steps = Math.Max(1, (int)Math.Ceiling(seconds / 0.004));
        double h = seconds / steps;
        for (int i = 0; i < steps; i++)
        {
            Velocity += (stiffness * (Target - Value) - damping * Velocity) * h;
            Value += Velocity * h;
        }
        if (Settled) { Value = Target; Velocity = 0; }
    }

    public void Jump(double value) { Value = Target = value; Velocity = 0; }
}
