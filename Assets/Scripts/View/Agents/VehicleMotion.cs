using UnityEngine;

namespace HarvestSim.Visualization
{
    public struct VehiclePose
    {
        public Vector3 Position;
        /// <summary>Ángulo en Y, en grados (N 0, E 90, S 180, O 270).</summary>
        public float Heading;
        public Quaternion Rotation => Quaternion.Euler(0, Heading, 0);
    }

    /// <summary>
    /// Pose visual entre dos ticks consecutivos. Es solo presentación: en t=0 y t=1 devuelve exactamente
    /// la pose del trace, y entre ambos dibuja una trayectoria creíble para la máquina.
    /// </summary>
    public static class VehicleMotion
    {
        /// <summary>Parte del tick que dura el giro en el sitio cuando el agente invierte su dirección.</summary>
        public const float PivotFraction = .4f;
        /// <summary>Largo relativo de las tangentes de Hermite en un giro de 90°.</summary>
        public const float TurnTangent = .9f;

        public static VehiclePose At(FieldGrid grid, AgentState state) => new()
        {
            Position = grid.CellToWorld(state.X, state.Y), Heading = FieldGrid.Heading(state.Orientation)
        };

        public static VehiclePose Sample(FieldGrid grid, AgentState from, AgentState to, float t)
        {
            if (t <= 0) return At(grid, from);
            if (t >= 1) return At(grid, to);
            var start = grid.CellToWorld(from.X, from.Y);
            var end = grid.CellToWorld(to.X, to.Y);
            float startHeading = FieldGrid.Heading(from.Orientation);
            float endHeading = FieldGrid.Heading(to.Orientation);
            if (from.X == to.X && from.Y == to.Y)
                return new VehiclePose { Position = start, Heading = Mathf.LerpAngle(startHeading, endHeading, Smooth(t)) };

            var travel = end - start;
            float moveHeading = Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
            float turn = Mathf.DeltaAngle(startHeading, moveHeading);
            bool singleStep = Mathf.Abs(to.X - from.X) + Mathf.Abs(to.Y - from.Y) == 1;

            if (!singleStep || Mathf.Abs(turn) < 1f)
                return new VehiclePose { Position = Vector3.Lerp(start, end, t), Heading = Mathf.LerpAngle(moveHeading, endHeading, t) };

            if (Mathf.Abs(turn) > 135f)
            {
                // Reversa de 180°: gira en su celda y después avanza. Siempre gira a la derecha para que sea consistente.
                if (t < PivotFraction)
                    return new VehiclePose { Position = start, Heading = startHeading + 180f * Smooth(t / PivotFraction) };
                float u = (t - PivotFraction) / (1 - PivotFraction);
                return new VehiclePose { Position = Vector3.Lerp(start, end, u), Heading = moveHeading };
            }

            // Giro de 90°: curva de Hermite que sale con el rumbo anterior y llega con el nuevo.
            float distance = travel.magnitude;
            var m0 = FieldGrid.Forward(startHeading) * distance * TurnTangent;
            var m1 = FieldGrid.Forward(moveHeading) * distance * TurnTangent;
            var position = Hermite(start, m0, end, m1, t);
            var velocity = HermiteDerivative(start, m0, end, m1, t);
            float heading = velocity.sqrMagnitude > 1e-8f ? Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg : moveHeading;
            return new VehiclePose { Position = position, Heading = heading };
        }

        static float Smooth(float t) => t * t * (3 - 2 * t);

        static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
        }

        static Vector3 HermiteDerivative(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t;
            return (6 * t2 - 6 * t) * p0 + (3 * t2 - 4 * t + 1) * m0 + (-6 * t2 + 6 * t) * p1 + (3 * t2 - 2 * t) * m1;
        }
    }
}
