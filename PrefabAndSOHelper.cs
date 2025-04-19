namespace OCG
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
#if UNITY_EDITOR
    using UnityEditor;
#endif
    using UnityEngine;
    using Object = UnityEngine.Object;
    public static class PrefabAndSOHelper
    {
        [SerializeField]
        private static string AssetFolder = "Assets/Prefabs";

        public static string Pluralize(string assetType)
        {
            var plural = "s";
            if (assetType.Last() == 's' || assetType.Last() == 'x')
            {
                plural = "es";
            }

            return assetType.Trim() + plural;
        }
        private static string GetAssetFolder(string assetType)
        {
            return $"{AssetFolder}/{Pluralize(assetType)}/";
        }

        private static string GetAssetPath(Type assetType, string SOName)
        {
            return $"{GetAssetFolder(assetType.ToString())}{SOName.Trim()}{GetFileExtension(assetType)}";
        }

        private static string GetFileExtension(Type assetType)
        {
            var fileExtension = ".prefab";
            if (assetType != null && ReflectionHelpers.InheritsFrom(assetType, typeof(ScriptableObject)))
            {
                fileExtension = ".asset";
            }
            return fileExtension;
        }
        public static void GetSOsFromFile(string name, List<UnityEngine.Object> results, Func<string, Type> getTypeFunc)
        {
#if UNITY_EDITOR
            var typeName = name;
            var folderName = name;
            if (name.LastIndexOf(":", StringComparison.Ordinal) > 0)
            {
                folderName = name.Substring(0, typeName.LastIndexOf(":", StringComparison.Ordinal));
                typeName = name.Substring(typeName.LastIndexOf(":", StringComparison.Ordinal) + 1);
            }

            var type = getTypeFunc(typeName);
            string fileExtension = GetFileExtension(type);

            var files = Directory.GetFiles(GetAssetFolder(folderName), "*" + fileExtension, SearchOption.AllDirectories).ToList();
            foreach (var file in files)
            {
                var assetPath = file.Replace(Application.dataPath, "").Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath(assetPath, type);
                if (asset != null)
                {
                    results.Add(asset);
                }
            }
#endif
        }

        private static Object GetOrCreateSO(SerializedObjectPrefabs prefabs, Type SOType, string SOName, bool createObject)
        {
            var SO = prefabs.GetSO(SOType, SOName);
            if (SO == null && createObject)
            {
                SO = ScriptableObject.CreateInstance(SOType);
                AssetDatabase.CreateAsset(SO, GetAssetPath(SOType, SOName));
                AddSO(prefabs, SO as ScriptableObject, SOType, SOName);
            }

            if (SO != null)
            {
                prefabs.MarkSOAsSynced(SO);
            }

            return SO;
        }

        public static void AddSO(SerializedObjectPrefabs prefabs, UnityEngine.Object so, Type soType, string soName)
        {
            if (so == null)
            {
                return;
            }

            prefabs.cachedObjects = null;
            Undo.RecordObject(prefabs, "Added SO.");
            prefabs.objects.Add(so);

            EditorUtility.SetDirty(prefabs);
        }

        public static string SerializeSpriteAsset(Sprite source)
        {
#if UNITY_EDITOR
            if (source == null)
            {
                return null;
            }
            var path = AssetDatabase.GetAssetPath(source);
            if (path == null)
            {
                return null;
            }
            return path;
#else
return null;
#endif
        }

        public static void SavePrefab<T>(T newItem, string prefabName) where T : Component
        {
#if UNITY_EDITOR
            if (PrefabUtility.IsPartOfPrefabAsset(newItem.gameObject))
            {
                PrefabUtility.SavePrefabAsset(newItem.gameObject);
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(newItem.gameObject, GetAssetPath(typeof(T), prefabName));
            }
#endif
        }

        public static object GetOrCreateSOInEditor(SerializedObjectPrefabs prefabs, Type objectType, string name, bool createObject, object oldObject)
        {
#if UNITY_EDITOR
            if (typeof(ScriptableObject).IsAssignableFrom(objectType))
            {
                var foundObject = PrefabAndSOHelper.GetOrCreateSO(prefabs, objectType, name, createObject);
                if (foundObject != null)
                {
                    EditorUtility.SetDirty((Object)foundObject);
                }

                return foundObject;
            }
            else
            {
                if (oldObject != null)
                {
                    return oldObject;
                }
                else
                {
                    return Activator.CreateInstance(objectType);
                }
            }
#else 
return null;
#endif
        }
    }
}
