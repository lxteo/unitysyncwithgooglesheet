namespace OCG
{
    using System.Reflection;
    using UnityEditor;
    using FMODUnity;
    using System;
    using System.Collections;
    using System.Globalization;
    using System.Linq;
    using UnityEngine;
    using System.Collections.Generic;

    public abstract class DownloadDataComponent
    {

    }

    public class GameDataSyncState
    {
        public GameDataSyncMode Mode;
        public Dictionary<string, object> DoneObjects;
        public GetOrCreateObjectFunc GetOrCreateObjectMethod;
        public SerializedObjectPrefabs Prefabs;
        public Func<string, Type> GetTypeFunc;
        public Type GetType(string v)
        {
            return GetTypeFunc(v);
        }
        public object GetOrCreateObject(Type objectType, string name, bool create, object oldObject)
        {
            return GetOrCreateObjectMethod(this.Prefabs, objectType, name, create, oldObject);
        }

        public delegate object GetOrCreateObjectFunc(SerializedObjectPrefabs prefabs, Type objectType, string name, bool create, object oldObject);
    }
    public static class GameDataSyncHelper
    {
        public static object GetDefault(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }
            else if (type.IsArray)
            {
                var empty = Array.CreateInstance(type.GetElementType(), 0);
                return empty;

            }
            return null;
        }

        public static object ParseValue(Type fieldType, GameDataField field, GameDataSyncState state)
        {
            var value = field.Value;
            if (fieldType == typeof(float))
            {
                return float.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, NumberFormatInfo.InvariantInfo);
            }
            else if (fieldType == typeof(int))
            {
                return int.Parse(value, NumberStyles.Integer, NumberFormatInfo.InvariantInfo);
            }
            else if (fieldType == typeof(ushort))
            {
                return ushort.Parse(value, NumberStyles.Integer, NumberFormatInfo.InvariantInfo);
            }
            else if (fieldType.IsEnum)
            {
                value = value.Replace("  ", " ");
                value = value.Replace(", ", ",");
                value = value.Replace(" ", ", ");
                return Enum.Parse(fieldType, value);
            }
            else if (fieldType.BaseType == typeof(DownloadDataComponent))
            {
                return CreateDownloadDataComponent(value, state);
            }
            else if (Helpers.InheritsFrom(fieldType, typeof(ScriptableObject)) || Helpers.InheritsFrom(fieldType, typeof(MonoBehaviour)))
            {
                var found = state.GetOrCreateObject( fieldType, value, false, null);
                if (found == null)
                {
                    Debug.LogError($"Couldn't find SO: {fieldType}, {value}");
                    return null;
                }

                return found;
            }
            else if (fieldType == typeof(EventReference))
            {
                var result = AudioManager.FindAudio(value);
                if (result.Guid.IsNull && !string.IsNullOrEmpty(value))
                {
                    Debug.LogError($"Couldn't find audio: {value}");
                }
                return result;
            }
            else if (fieldType.IsArray)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    var empty = Array.CreateInstance(fieldType.GetElementType(), 0);
                    return empty;
                }
                var splitVals = value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => ParseValue(fieldType.GetElementType(), new GameDataField(c.Trim()), state))
                    .ToArray();
                var result = Array.CreateInstance(fieldType.GetElementType(), splitVals.Length);
                Array.Copy(splitVals, result, result.Length);
                return result;
            }
            else if (fieldType == typeof(Vector3))
            {
                var floats = value.Split(',').Select(c => float.Parse(c.Trim())).ToArray();
                if (floats.Length != 3)
                {
                    Debug.LogError("invalid vector 3");
                }
                return new Vector3(floats[0], floats[1], floats[2]);
            }
            else if (fieldType == typeof(bool))
            {
                return bool.Parse(value);
            }
            else
            {
                return value;
            }
        }

        public static DownloadDataComponent CreateDownloadDataComponent(string tt, GameDataSyncState state)
        {
            if (string.IsNullOrEmpty(tt))
            {
                return null;
            }
            ParseComponentType(tt, out string title, out Type foundType);
            if (foundType == null)
            {
                Debug.LogError("Could not find download component type " + title);
                return null;
            }

            var result = Activator.CreateInstance(foundType) as DownloadDataComponent;

            if (result == null)
            {
                Debug.LogError("Download component invalid.");
                return null;
            }

            ParseComponentFields(tt, foundType, result, state);

            return result;
        }

        private static void ParseComponentFields(string tt, Type foundType, object result, GameDataSyncState state)
        {
            if (tt.IndexOf("}", StringComparison.Ordinal) == -1 || tt.IndexOf("{", StringComparison.Ordinal) == -1)
            {
                Debug.LogError("invalid component: " + tt);
                return;
            }
            var init = tt.Substring(tt.IndexOf("{", StringComparison.Ordinal) + 1, tt.IndexOf("}", StringComparison.Ordinal) - tt.IndexOf("{", StringComparison.Ordinal) - 1);
            var properties = init.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var property in properties)
            {
                var kvp = property.Split(':');
                if (kvp.Length != 2)
                {
                    Debug.LogError($"Colon not found for {foundType}, {property}");
                    continue;
                }
                var fieldName = kvp[0].Replace(" ", "");
                var field = foundType.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (field == null)
                {
                    Debug.LogError($"Field not found for {foundType}.{fieldName}");
                    continue;
                }
                field.SetValue(result,
                    GameDataSyncHelper.ParseValue(field.FieldType, new GameDataField(kvp[1]), state));

            }
        }

        private static void ParseComponentType(string tt, out string title, out Type foundType)
        {
            title = tt.Substring(0, tt.IndexOf("{", StringComparison.Ordinal)).Trim();
            foundType = Helpers.GetType(title);
        }

        public static string SerializeValue(Type fieldType, object value)
        {
            if (fieldType == typeof(float))
            {
                return ((float)value).ToString("0.####");
            }
            else if (fieldType == typeof(int))
            {
                return ((int)value).ToString();
            }
            else if (fieldType == typeof(ushort))
            {
                return ((ushort)value).ToString();
            }
            else if (fieldType.IsEnum)
            {
                if ((int)value == 0)
                {
                    return null;
                }
                return Enum.Format(fieldType, value, "g");
            }
            else if (fieldType.BaseType == typeof(DownloadDataComponent))
            {
                return SerializeDownloadDataComponent(fieldType, (DownloadDataComponent)value);
            }
            else if (Helpers.InheritsFrom(fieldType, typeof(ScriptableObject)) || Helpers.InheritsFrom(fieldType, typeof(MonoBehaviour)))
            {
                return SerializeSO(fieldType, value as UnityEngine.Object);
            }
            else if (fieldType == typeof(EventReference))
            {
#if UNITY_EDITOR
                return ((EventReference)value).Path;
#else
return AudioManager.GetPath((EventReference)value);
#endif

            }
            else if (fieldType.IsArray)
            {
                if (value == null)
                {
                    return null;
                }
                var arr = ((IEnumerable)value).Cast<object>();
                if (arr == null || arr.Count() == 0)
                {
                    return null;
                }

                return string.Join(",", arr.Select(c => SerializeValue(fieldType.GetElementType(), c)));
            }
            else if (fieldType == typeof(Vector3))
            {
                var vec = (Vector3)value;
                return $"{vec.x:0.####},{vec.y:0.####},{vec.z:0.####}";
            }
            else if (fieldType == typeof(string))
            {
                var str = (string)value;
                if (!string.IsNullOrEmpty(str) && (str[0] == '=' || str[0] == '+'))
                {
                    return "'" + str;
                }
                else
                {
                    return str;
                }
            }
            else
            {
                return value?.ToString();
            }
        }

        public static string SerializeDownloadDataComponent(Type fieldType, DownloadDataComponent component)
        {
            if (component == null)
            {
                return null;
            }

            var componentType = component.GetType();
            var fields = componentType.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                                                 System.Reflection.BindingFlags.IgnoreCase);

            var result = componentType.Name + "{";
            foreach (var field in fields)
            {
                if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
                {
                    continue;
                }
                result += $"{field.Name}:{GameDataSyncHelper.SerializeValue(field.FieldType, field.GetValue(component))};";
            }

            result += "}";
            return result;
        }

        public static string SerializeSO(Type AssetType, UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }
            return obj.name;
        }

        public static void CreateInstanceComponents(GameObject result, string tt, GameDataSyncState state)
        {
            if (string.IsNullOrEmpty(tt))
            {
                return;
            }

            ParseComponentType(tt, out string title, out Type foundType);
            if (foundType == null)
            {
                Debug.LogError("Could not find component type " + title);
                return;
            }

            var component = result.GetComponent(foundType);
            if (component == null)
            {
                component = result.AddComponent(foundType);
            }

            ParseComponentFields(tt, foundType, component, state);
        }

    }
}
