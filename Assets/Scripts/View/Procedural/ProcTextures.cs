using UnityEngine;

namespace HarvestSim.Visualization
{
    /// <summary>Texturas pequeñas generadas por código. Se guardan como assets desde el editor para no recalcularlas.</summary>
    public static class ProcTextures
    {
        public const int PaletteColumns = 8, PaletteRows = 8;
        public enum CornPart { StalkBottom = 0, StalkTop = 1, LeafBase = 2, LeafTip = 3, Husk = 4, Tassel = 5, Stubble = 6 }

        /// <summary>Paleta del maíz seco listo para cosecha: filas por parte de la planta, columnas de claro a oscuro.</summary>
        public static Texture2D CornPalette()
        {
            var colors = new[]
            {
                new Color(.30f, .25f, .17f), // tallo abajo (oclusión)
                new Color(.64f, .55f, .38f), // tallo arriba
                new Color(.52f, .45f, .31f), // hoja base
                new Color(.76f, .68f, .52f), // hoja punta
                new Color(.82f, .73f, .54f), // totomoxtle (hoja de la mazorca)
                new Color(.55f, .42f, .27f), // espiga
                new Color(.58f, .50f, .36f), // rastrojo
                new Color(.50f, .43f, .30f),
            };
            var texture = new Texture2D(PaletteColumns, PaletteRows, TextureFormat.RGBA32, false, false)
            { name = "CornPalette", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int row = 0; row < PaletteRows; row++)
                for (int column = 0; column < PaletteColumns; column++)
                {
                    float shade = 1.12f - column * .045f;
                    // Las columnas claras tiran a paja y algunas medias conservan un poco de verde seco.
                    var tint = column switch { 2 => new Color(.97f, 1.02f, .9f), 5 => new Color(1.02f, .98f, .95f), _ => Color.white };
                    var c = colors[row] * shade * tint; c.a = 1;
                    texture.SetPixel(column, row, c);
                }
            texture.Apply(false, false);
            return texture;
        }

        public static Vector2 PaletteUv(int column, CornPart part, float along = 0)
        {
            float v = ((int)part + .5f + along) / PaletteRows;
            return new Vector2((Mathf.Clamp(column, 0, PaletteColumns - 1) + .5f) / PaletteColumns, v);
        }

        /// <summary>Mapa normal de lámina corrugada (crestas verticales) para silos y tanques.</summary>
        public static Texture2D CorrugatedNormal(int width = 64, int height = 4)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            { name = "CorrugatedNormal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < width; x++)
            {
                float slope = Mathf.Cos(x / (float)width * Mathf.PI * 2) * .55f;
                var n = new Vector3(slope, 0, 1).normalized;
                var c = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
                for (int y = 0; y < height; y++) texture.SetPixel(x, y, c);
            }
            texture.Apply(true, false);
            return texture;
        }

        /// <summary>Ruido gris (media 0.5) para variar el color del suelo a gran escala (mapa de detalle x2 de URP Lit).</summary>
        public static Texture2D MacroNoise(int size = 256, int seed = 5)
        {
            // Lineal: URP multiplica el mapa de detalle por 2 sin convertir de sRGB.
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            { name = "MacroRuido", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var random = new System.Random(seed);
            float ox = random.Next(0, 1000), oy = random.Next(0, 1000);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Ruido periódico: se muestrea sobre un toro para que la textura repita sin costuras.
                    float n = 0, amplitude = 1, total = 0;
                    float u = x / (float)size * Mathf.PI * 2, v = y / (float)size * Mathf.PI * 2;
                    for (int octave = 0; octave < 4; octave++)
                    {
                        float frequency = 2 << octave;
                        n += amplitude * Mathf.PerlinNoise(ox + (Mathf.Cos(u) + Mathf.Sin(v) * .6f) * frequency * .5f,
                                                           oy + (Mathf.Sin(u) + Mathf.Cos(v)) * frequency * .5f);
                        total += amplitude; amplitude *= .5f;
                    }
                    float g = Mathf.Clamp01(.5f + (n / total - .5f) * .55f);
                    texture.SetPixel(x, y, new Color(g, g, g, 1));
                }
            texture.Apply(true, false);
            return texture;
        }

        /// <summary>Anillo suave para marcar a cada agente en el suelo (alfa en el borde).</summary>
        public static Texture2D Ring(int size = 128)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            { name = "AgentRing", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude / (size / 2f);
                    float ring = Mathf.Clamp01(1 - Mathf.Abs(d - .86f) / .09f);
                    float fill = d < .8f ? .14f * (1 - d) : 0;
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(ring + fill)));
                }
            texture.Apply(true, false);
            return texture;
        }
    }
}
