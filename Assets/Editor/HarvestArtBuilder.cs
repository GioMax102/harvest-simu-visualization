using System.IO;
using HarvestSim.Visualization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HarvestSim.Editor
{
    /// <summary>
    /// Genera (o actualiza) el arte de la vista realista en Assets/Art: materiales URP, texturas procedurales,
    /// cielo, perfil de postproceso y el asset HarvestArt que usa WorldView. Es idempotente.
    /// </summary>
    public static class HarvestArtBuilder
    {
        public const string ArtPath = "Assets/Art/HarvestArt.asset";
        public const string MaterialsFolder = "Assets/Art/Materials";
        public const string GeneratedFolder = "Assets/Art/Generated";
        public const string TexturesFolder = "Assets/Art/Environment/Textures";
        public const string SkyPath = "Assets/Art/Sky/kloofendal_48d_partly_cloudy_puresky_2k.hdr";
        public const string VolumePath = "Assets/Art/Generated/HarvestVolume.asset";
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Harvest Sim/Generar arte")]
        public static HarvestArt Build()
        {
            Directory.CreateDirectory(MaterialsFolder);
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh();
            ConfigureTextures();

            var art = AssetDatabase.LoadAssetAtPath<HarvestArt>(ArtPath);
            if (!art) { art = ScriptableObject.CreateInstance<HarvestArt>(); AssetDatabase.CreateAsset(art, ArtPath); }

            var macro = SaveTexture(ProcTextures.MacroNoise(), "MacroRuido.asset");
            art.soilMaterial = Ground("Suelo_Tierra", "farm_soil", 3.5f, .08f);
            art.stubbleMaterial = Ground("Suelo_Rastrojo", "withered_grass", 4f, .06f, new Color(.86f, .8f, .7f));
            art.roadMaterial = Ground("Suelo_Camino", "gravel_road", 5f, .12f, new Color(.95f, .95f, .98f));
            art.grassMaterial = Ground("Suelo_Pasto", "grass_ground", 7f, .08f, new Color(.84f, .9f, .74f));
            foreach (var ground in new[] { art.soilMaterial, art.stubbleMaterial, art.roadMaterial, art.grassMaterial }) Macro(ground, macro, 110);
            art.canopyMaterial = Ground("Dosel_Maiz", "withered_grass", 2.5f, .05f, new Color(.98f, .86f, .62f));
            Macro(art.canopyMaterial, macro, 60);
            art.canopyMaterial.enableInstancing = true;
            art.concreteMaterial = Ground("Suelo_Concreto", "concrete_floor_02", 4f, .2f);

            var corrugated = SaveTexture(ProcTextures.CorrugatedNormal(), "Corrugado.asset");
            art.steelMaterial = Lit("Acero_Galvanizado", new Color(.74f, .76f, .78f), .78f, .55f, corrugated, .7f);
            art.paintedMetalMaterial = Lit("Metal_Pintado", new Color(.86f, .85f, .8f), .25f, .45f);
            art.foliageMaterial = Lit("Follaje", new Color(.25f, .33f, .14f), 0, .08f);
            art.barkMaterial = Lit("Corteza", new Color(.27f, .21f, .16f), 0, .1f);
            art.rockMaterial = Lit("Roca", new Color(.47f, .45f, .41f), 0, .15f);
            art.woodMaterial = Lit("Madera", new Color(.42f, .33f, .24f), 0, .1f);
            art.grainMaterial = Lit("Grano", new Color(.93f, .72f, .2f), 0, .25f);

            var palette = SaveTexture(ProcTextures.CornPalette(), "PaletaMaiz.asset");
            art.cornMaterial = Lit("Maiz", Color.white, 0, .12f);
            art.cornMaterial.SetTexture("_BaseMap", palette);
            art.cornMaterial.SetFloat("_Cull", (float)CullMode.Off);
            art.cornMaterial.doubleSidedGI = true;
            art.cornMaterial.enableInstancing = true;
            EditorUtility.SetDirty(art.cornMaterial);

            var ring = SaveTexture(ProcTextures.Ring(), "Anillo.asset");
            art.markerMaterial = Transparent("Marcador_Agente", ring);
            art.lineMaterial = LineMaterial();

            art.harvesterPrefab = AssetDatabase.LoadAssetAtPath<AgentView>("Assets/Prefabs/Harvest/Harvester.prefab");
            art.cartPrefab = AssetDatabase.LoadAssetAtPath<AgentView>("Assets/Prefabs/Harvest/Cart.prefab");
            art.labelFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            art.labelMaterial = LabelMaterial(art.labelFont);
            EditorUtility.SetDirty(art);

            BuildVolumeProfile();
            AssetDatabase.SaveAssets();
            return art;
        }

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                bool normal = path.Contains("_nor_gl");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType == type && importer.maxTextureSize == 1024 && importer.anisoLevel == 8) continue;
                importer.textureType = type;
                importer.maxTextureSize = 1024;
                importer.anisoLevel = 8;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            if (AssetImporter.GetAtPath(SkyPath) is TextureImporter sky && (sky.mipmapEnabled || sky.maxTextureSize != 2048))
            {
                sky.textureShape = TextureImporterShape.Texture2D;
                sky.mipmapEnabled = false;
                sky.wrapModeU = TextureWrapMode.Repeat; sky.wrapModeV = TextureWrapMode.Clamp;
                sky.maxTextureSize = 2048;
                sky.SaveAndReimport();
            }
        }

        static Texture2D SaveTexture(Texture2D texture, string file)
        {
            string path = GeneratedFolder + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(texture, existing);
                Object.DestroyImmediate(texture);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        static Material LoadOrCreate(string name, string shaderName)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (!shader) throw new System.InvalidOperationException("No se encuentra el shader " + shaderName);
            if (!material) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
            else if (material.shader != shader) material.shader = shader;
            return material;
        }

        static Material Ground(string name, string texture, float tileMeters, float smoothness, Color? tint = null)
        {
            var material = Lit(name, tint ?? Color.white, 0, smoothness);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{texture}_diff_1k.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{texture}_nor_gl_1k.jpg");
            material.SetTexture("_BaseMap", albedo);
            if (normal) { material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", 1); material.EnableKeyword("_NORMALMAP"); }
            material.SetTextureScale("_BaseMap", Vector2.one / tileMeters);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Variación de color a gran escala con el mapa de detalle x2 (gris medio = sin cambio).</summary>
        static void Macro(Material material, Texture2D noise, float meters)
        {
            material.SetTexture("_DetailAlbedoMap", noise);
            material.SetFloat("_DetailAlbedoMapScale", 1);
            material.SetTextureScale("_DetailAlbedoMap", Vector2.one / meters);
            material.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(material);
        }

        static Material Lit(string name, Color color, float metallic, float smoothness, Texture2D normal = null, float normalScale = 1)
        {
            var material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (normal)
            {
                material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", normalScale); material.EnableKeyword("_NORMALMAP");
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Transparent(string name, Texture2D texture)
        {
            var material = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LineMaterial()
        {
            // Sprites/Default respeta el color por vértice del LineRenderer y no recibe luz.
            var material = LoadOrCreate("Linea_Coordinacion", "Sprites/Default");
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Copia del material de la fuente con contorno oscuro: legible sobre maíz claro y sobre pasto.</summary>
        static Material LabelMaterial(TMP_FontAsset fontAsset)
        {
            if (!fontAsset) return null;
            string path = MaterialsFolder + "/Placa_Agente.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(fontAsset.material) { name = "Placa_Agente" }; AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(fontAsset.material);
            material.EnableKeyword("OUTLINE_ON");
            material.SetFloat("_OutlineWidth", .22f);
            material.SetColor("_OutlineColor", new Color32(20, 24, 22, 255));
            material.SetFloat("_FaceDilate", .1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, VolumePath); }
            T Component<T>() where T : VolumeComponent
            {
                if (!profile.TryGet(out T component))
                {
                    component = profile.Add<T>(true);
                    component.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(component, profile);
                }
                component.active = true;
                return component;
            }
            var tonemapping = Component<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.ACES);
            var color = Component<ColorAdjustments>();
            color.postExposure.Override(.15f);
            color.contrast.Override(12);
            color.saturation.Override(-4);
            var white = Component<WhiteBalance>();
            white.temperature.Override(0);
            var bloom = Component<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(.25f);
            bloom.scatter.Override(.6f);
            var vignette = Component<Vignette>();
            vignette.intensity.Override(.18f);
            vignette.smoothness.Override(.45f);
            EditorUtility.SetDirty(profile);
        }

        /// <summary>Busca la dirección del sol en el HDRI (píxel más brillante) y el color promedio del horizonte.</summary>
        public static (Vector3 sunDirection, Color horizon) AnalyzeSky()
        {
            var importer = AssetImporter.GetAtPath(SkyPath) as TextureImporter;
            if (!importer) return (new Vector3(-.4f, .7f, -.6f).normalized, new Color(.75f, .8f, .86f));
            bool readable = importer.isReadable;
            var compression = importer.textureCompression;
            importer.isReadable = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyPath);
            var pixels = texture.GetPixels();
            int width = texture.width, height = texture.height;
            float best = -1; int bestIndex = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                float l = pixels[i].r * .2126f + pixels[i].g * .7152f + pixels[i].b * .0722f;
                if (l > best) { best = l; bestIndex = i; }
            }
            // Horizonte: promedio de una franja justo sobre la línea media (v ≈ 0.52).
            var horizon = Color.black; int samples = 0;
            int row = Mathf.RoundToInt(height * .52f);
            for (int x = 0; x < width; x += 4) { horizon += pixels[row * width + x]; samples++; }
            horizon /= Mathf.Max(1, samples);
            importer.isReadable = readable; importer.textureCompression = compression;
            importer.SaveAndReimport();
            // Inversa de ToRadialCoords de Skybox/Panoramic con rotación 0.
            float u = (bestIndex % width + .5f) / width, v = (bestIndex / width + .5f) / height;
            float longitude = (.5f - u) * 2 * Mathf.PI, latitude = (1 - v) * Mathf.PI;
            var direction = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
            float max = Mathf.Max(horizon.r, Mathf.Max(horizon.g, horizon.b));
            if (max > 1) horizon /= max;
            horizon.a = 1;
            return (direction.normalized, horizon);
        }
    }
}
