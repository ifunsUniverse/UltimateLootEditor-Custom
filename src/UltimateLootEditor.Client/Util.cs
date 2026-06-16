// Util.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ULE.SpawnEditor
{
    internal static class Util
    {
        private static readonly Dictionary<int, Material> _sphereMaterials = new Dictionary<int, Material>();
        private static Mesh _sharedSphereMesh;

        public static readonly Color Yellow = new Color(1f, 0.92f, 0.016f, 0.82f);
        public static readonly Color Cyan = new Color(0f, 1f, 1f, 0.88f);
        public static readonly Color Green = new Color(0.2f, 1f, 0.2f, 0.88f);
        public static readonly Color QuestOrange = new Color(1f, 0.55f, 0.1f, 0.95f);

        public static Mesh GetOrCreateSphereMesh()
        {
            if (_sharedSphereMesh != null)
            {
                return _sharedSphereMesh;
            }

            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                var filter = temp.GetComponent<MeshFilter>();
                _sharedSphereMesh = filter != null ? filter.sharedMesh : null;
            }
            finally
            {
                UnityEngine.Object.Destroy(temp);
            }

            return _sharedSphereMesh;
        }

        public static Material GetOrCreateSphereMaterial(Color color, string materialName)
        {
            var key = ColorToKey(color);
            if (_sphereMaterials.TryGetValue(key, out var existing) && existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Diffuse");
            }

            var material = new Material(shader)
            {
                name = materialName,
                renderQueue = 4000
            };

            SetMaterialColor(material, color);
            material.SetOverrideTag("RenderType", "Transparent");
            TrySetInt(material, "_SrcBlend", (int)BlendMode.SrcAlpha);
            TrySetInt(material, "_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            TrySetInt(material, "_ZWrite", 0);
            TrySetInt(material, "_ZTest", (int)CompareFunction.Always);
            TrySetInt(material, "_Cull", (int)CullMode.Off);
            material.EnableKeyword("_ALPHABLEND_ON");

            _sphereMaterials[key] = material;
            return material;
        }

        public static string GenerateComposedKey()
        {
            return $"ULE:{Guid.NewGuid():N}";
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", color);
            }

            material.color = color;
        }

        private static void TrySetInt(Material material, string propertyName, int value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetInt(propertyName, value);
            }
        }

        private static int ColorToKey(Color color)
        {
            var c32 = (Color32)color;
            return (c32.r << 24) | (c32.g << 16) | (c32.b << 8) | c32.a;
        }
    }
}
