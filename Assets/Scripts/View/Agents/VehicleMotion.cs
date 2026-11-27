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

        /// <summary>
        /// 2.4: pose sobre la curva de una vuelta de cabecera (evento TRAYECTORIA, decidida por Python con Dubins),
        /// en la fracción <paramref name="u"/> (0..1) de su largo. Entre los puntos muestreados usa una spline de
        /// Catmull-Rom (curva paramétrica que pasa por todos los puntos); el rumbo es el de la curva de Dubins, que viene
        /// en cada punto del trace.
        /// Los puntos vienen en metros del marco de la cuadrícula: (x, y) → (x, h, −y) en Unity.
        /// </summary>
        public static VehiclePose AlongTrajectory(FieldGrid grid, Newtonsoft.Json.Linq.JArray points, float u)
        {
            int n = points.Count;
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) p[i] = new Vector2((float)points[i][0], -(float)points[i][1]);
            var lengths = new float[n];
            for (int i = 1; i < n; i++) lengths[i] = lengths[i - 1] + Vector2.Distance(p[i - 1], p[i]);
            float target = Mathf.Clamp01(u) * lengths[n - 1];
            int k = 1;
            while (k < n - 1 && lengths[k] < target) k++;
            float span = Mathf.Max(1e-6f, lengths[k] - lengths[k - 1]);
            float t = Mathf.Clamp01((target - lengths[k - 1]) / span);
            Vector2 p0 = p[Mathf.Max(0, k - 2)], p1 = p[k - 1], p2 = p[k], p3 = p[Mathf.Min(n - 1, k + 1)];
            Vector2 point = CatmullRom(p0, p1, p2, p3, t);
            // el rumbo exacto de la curva viene en el trace (radianes, 0 = +x de la cuadrícula); en Unity la
            // dirección (cos θ, −sin θ) en (x, z) da el ángulo atan2(x, z)
            float heading = Mathf.LerpAngle(Yaw((float)points[k - 1][2]), Yaw((float)points[k][2]), t);
            return new VehiclePose { Position = new Vector3(point.x, grid.GroundHeight(point.x, point.y), point.y), Heading = heading };
        }

        static float Yaw(float theta) => Mathf.Atan2(Mathf.Cos(theta), -Mathf.Sin(theta)) * Mathf.Rad2Deg;

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
        }

        /// <summary>Si la máquina está dando una vuelta de cabecera (2.4), su pose sobre la curva; si no, null.</summary>
        public static VehiclePose? OnTrajectory(FieldGrid grid, PlaybackState state, string agent, float fraction)
        {
            if (!state.Trajectories.TryGetValue(agent, out var active)) return null;
            int ticks = (int)active.path["ticks"];
            float u = (state.Tick - active.start + fraction) / ticks;
            if (u < 0 || u > 1) return null;
            return AlongTrajectory(grid, (Newtonsoft.Json.Linq.JArray)active.path["puntos"], u);
        }

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
