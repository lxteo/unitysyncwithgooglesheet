namespace OCG
{
    using System;
    using System.Collections.Generic;
#if UNITY_EDITOR
    using UnityEditor;
#endif
    using UnityEngine;
    using Object = UnityEngine.Object;

    public class SerializedObjectPrefabs : ScriptableObject
    {
        [SerializeField]
        private List<string> scriptableObjectsToSync;
        public List<string> ScriptableObjectsToSync => this.scriptableObjectsToSync;

        [SerializeField]
        private List<string> otherFolders;
        public List<string> OtherFolders => this.otherFolders;

        [SerializeField]
        public List<UnityEngine.Object> objects;

        public Dictionary<int, UnityEngine.Object> cachedObjects;

        public HashSet<UnityEngine.Object> doneObjects;
        protected void RefreshSOs(Func<string, Type> getTypeFunc)
        {
#if UNITY_EDITOR
            this.cachedObjects = null;
            this.doneObjects = new HashSet<Object>();

            var so = new SerializedObject(this);
            for (var i = this.objects.Count - 1; i >= 0; i -= 1)
            {
                if (this.objects[i] == null)
                {
                    this.objects.RemoveAt(i);
                }
                else
                {
                    doneObjects.Add(this.objects[i]);
                }
            }
            var oldIds = new HashSet<ushort>();
            var results = new List<UnityEngine.Object>();
            foreach (var s in this.ScriptableObjectsToSync)
            {
                results.Clear();
                oldIds.Clear();
                PrefabAndSOHelper.GetSOsFromFile(s, results, getTypeFunc);
                foreach (var result in results)
                {
                    if (!this.doneObjects.Contains(result))
                    {
                        this.objects.Add(result);
                        continue;
                    }
                    var idObj = result as ScriptableObjectWithId;
                    if (idObj == null)
                    {
                        continue;
                    }
                    if (idObj.Id != 0)
                    {
                        if (!oldIds.Add(idObj.Id))
                        {
                            idObj.Id = 0;
                        }
                    }
                }

                foreach (var result in results)
                {
                    var idObj = result as ScriptableObjectWithId;
                    if (idObj == null || oldIds.Contains(idObj.Id))
                    {
                        continue;
                    }

                    for (ushort i = 1; i < ushort.MaxValue; i += 1)
                    {
                        if (!oldIds.Contains(i))
                        {
                            oldIds.Add(i);
                            var soFound = new SerializedObject(idObj);
                            idObj.Id = i;
                            soFound.Update();
                            soFound.ApplyModifiedProperties();
                            EditorUtility.SetDirty(idObj);
                            break;
                        }
                    }
                }
            }

            foreach (var s in this.OtherFolders)
            {
                results.Clear();
                PrefabAndSOHelper.GetSOsFromFile(s, results, getTypeFunc);
                foreach (var result in results)
                {
                    if (doneObjects.Contains(result))
                    {
                        continue;
                    }
                    this.objects.Add(result);
                }
            }

            so.Update();
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(this);
#endif
        }

        public UnityEngine.Object GetSO(Type soType, string s)
        {
            return GetSO(soType.ToString(), s);
        }

        public void ClearCache()
        {
            this.objects = new List<Object>();
            this.cachedObjects = null;
            this.doneObjects = new HashSet<Object>();
        }

        public UnityEngine.Object GetSO(string soType, string s)
        {
            if (cachedObjects == null)
            {
                BuildCachedObject();
            }

            if (cachedObjects.TryGetValue(GetKey(soType, s), out UnityEngine.Object result))
            {
                return result;
            }

            return null;
        }

        private void AddObjectRuntime(Object obj)
        {
            this.objects.Add(obj);
            if (cachedObjects == null)
            {
                BuildCachedObject();
            }

            this.cachedObjects[GetKey(obj.GetType().ToString(), obj.name)] = obj;

        }

        private int GetKey(string soType, string s)
        {
            unchecked
            {
                return 17 * soType.GetHashCode() + 23 * s.GetHashCode();
            }
        }

        private void BuildCachedObject()
        {
            cachedObjects = new Dictionary<int, UnityEngine.Object>();
            for (var i = this.objects.Count - 1; i >= 0; i--)
            {
                if (this.objects[i] == null)
                {
                    this.objects.RemoveAt(i);
                    continue;
                }
                cachedObjects[GetKey(objects[i].GetType().ToString(), objects[i].name)] = objects[i];
            }
        }

        public Object[] GetSOs(Type t)
        {
            if (cachedObjects == null)
            {
                BuildCachedObject();
            }
            var result = new List<Object>();

            foreach (var scriptableObject in this.objects)
            {
                if (scriptableObject.GetType() == t)
                {
                    result.Add(scriptableObject);
                }
            }
            return result.ToArray();
        }

        public T[] GetSOIdLookupArray<T>() where T : ScriptableObjectWithId
        {
            if (cachedObjects == null)
            {
                BuildCachedObject();
            }
            var maxId = 0;
            foreach (var scriptableObject in this.objects)
            {
                var found = scriptableObject as T;
                if (found != null)
                {
                    maxId = Mathf.Max(maxId, found.Id);
                }
            }
            var result = new T[maxId + 1];

            foreach (var scriptableObject in this.objects)
            {
                var found = scriptableObject as T;
                if (found != null)
                {
                    result[found.Id] = found;
                }
            }
            return result;
        }

        public T[] GetSOs<T>() where T : UnityEngine.Object
        {
            if (cachedObjects == null)
            {
                BuildCachedObject();
            }
            var result = new List<T>();

            foreach (var scriptableObject in this.objects)
            {
                var found = scriptableObject as T;
                if (found != null)
                {
                    result.Add(found);
                }
            }
            return result.ToArray();
        }

        public void MarkSOAsSynced(Object so)
        {
            this.doneObjects.Add(so);
        }

        public void OnFinishedDownload()
        {
            foreach (var o in this.objects)
            {
                if (o.GetType().BaseType != typeof(ScriptableObject))
                {
                    continue;
                }
                if (this.otherFolders.Contains(o.GetType().ToString()))
                {
                    continue;
                }
                if (!doneObjects.Contains(o))
                {
                    Debug.LogError("Found old object: " + o.ToString());
                }
            }
        }

        public object GetExistingObjectOrCreateRuntime(Type objectType, string name, bool createObject, object oldObject)
        {
            if (objectType.BaseType == typeof(ScriptableObject))
            {
                var result = GetSO(objectType, name);
                if (result == null)
                {
                    result = ScriptableObject.CreateInstance(objectType);
                    result.name = name;
                    AddObjectRuntime(result);
                }
                return result;
            }
            else
            {
                if (oldObject != null)
                {
                    return oldObject;
                }
                return Activator.CreateInstance(objectType);
            }
        }

    }
}
