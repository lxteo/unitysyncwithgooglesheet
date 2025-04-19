namespace OCG
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using UnityEngine;

    public interface IOrderedSO
    {
        int Order { get; set; }
    }

    public class NonUniqueNameAttribute : Attribute
    {
    }

    public class GameDataSheet
    {
        public GameDataSyncState State;

        public class FieldMapping
        {
            private readonly List<(MemberInfo member, FieldMapping mapping)> children;
            private readonly Type objectType;
            public Type ObjectType => this.objectType;
            public string Name => this.objectType.ToString();

            private readonly MemberInfo[] members;
            private readonly int start;

            public FieldMapping(Type objectType, MemberInfo[] members, int start)
            {
                this.objectType = objectType;
                this.members = members;
                this.start = start;
                this.children = new List<(MemberInfo, FieldMapping)>();
            }

            public void AddChild(MemberInfo childMember, FieldMapping mapping)
            {
                this.children.Add((childMember, mapping));
            }

            public List<object> ParseData(GameDataField[][] data, GameDataSyncState state, Array array = null)
            {
                var previousHeader = -1;
                GameDataField previousName = null;
                var result = new List<object>();
                var objectsFound = 0;

                for (var i = 0; i < data.Length; i++)
                {
                    object foundObject = null;
                    if (array != null && array.Length > objectsFound)
                    {
                        foundObject = array.GetValue(objectsFound);
                    }
                    objectsFound++;

                    var foundName = data[i][start];
                    if (!foundName.IsEmpty || (state.Mode == GameDataSyncMode.Upload && foundObject != null))
                    {
                        previousHeader = i;
                        previousName = foundName;
                    }
                    if (previousHeader < 0) continue;

                    var endOfBlock = i + 1 >= data.Length
                                   || !data[i + 1][start].IsEmpty;
                    if (!endOfBlock) continue;

                    var segment = data.Skip(previousHeader).Take(i + 1 - previousHeader).ToArray();
                    var parsed = ParseSingle(previousName, segment, i, state, foundObject);
                    if (parsed != null) result.Add(parsed);

                    previousHeader = -1;
                }

                return result;
            }

            private object ParseSingle(
                GameDataField name,
                GameDataField[][] data,
                int rowIndex,
                GameDataSyncState state,
                object foundObject = null
            )
            {
                var columns = data.First();

                if (members.Length == 0)
                {
                    Debug.LogError($"No members for {name.Value}");
                }

                if (CheckIfObjectExists(name, state, foundObject, out var existing) && state.Mode == GameDataSyncMode.Download)
                {
                    return existing;
                }

                if (foundObject == null || state.Mode == GameDataSyncMode.Download)
                {
                    foundObject = state.GetOrCreateObject(objectType, name.Value, true, foundObject);
                }

                // first, recurse into children
                foreach (var (member, childMap) in children)
                {
                    var existingArray = GetMemberValue(member, foundObject) as Array;
                    var childResults = childMap.ParseData(data, state, existingArray);

                    if (state.Mode == GameDataSyncMode.Download)
                    {
                        var elementType = GetMemberType(member).GetElementType();
                        var resultArray = Array.CreateInstance(elementType, childResults.Count);
                        Array.Copy(childResults.ToArray(), resultArray, childResults.Count);
                        SetMemberValue(member, foundObject, resultArray);
                    }
                }

                // now handle flat members
                for (var i = 0; i < members.Length; i++)
                {
                    var fieldCell = columns[i + start];
                    var member = members[i];
                    fieldCell.UpdateData(member, foundObject, state);
                }

                // ordering
                if (foundObject is IOrderedSO ord)
                    ord.Order = rowIndex;

                return foundObject;
            }

            public bool CheckIfObjectExists(
                GameDataField name,
                GameDataSyncState state,
                object oldObject,
                out object foundObject
            )
            {
                if (GetMemberType(members.First()) == typeof(string) && objectType.GetCustomAttribute<NonUniqueNameAttribute>() == null)
                {
                    var key = $"{objectType}_{name.Value}";
                    if (state.DoneObjects.TryGetValue(key, out foundObject))
                        return true;
                    state.DoneObjects[key] = oldObject;
                }
                foundObject = null;
                return false;
            }

            public FieldMapping FindWithType(Type t)
            {
                if (objectType == t) return this;
                foreach (var (_, childMap) in children)
                {
                    var f = childMap.FindWithType(t);
                    if (f != null) return f;
                }
                return null;
            }

            public GameDataField[] CreateRow(object so, GameDataSyncState state, int totalColumns)
            {
                var row = new GameDataField[totalColumns];
                // name col
                var name = (so as UnityEngine.Object)?.name ?? so.ToString();
                row[start] = new GameDataField(name);

                for (var i = 0; i < members.Length; i++)
                {
                    var member = members[i];
                    var raw = GetMemberValue(member, so);
                    var text = GameDataSyncHelper.SerializeValue(GetMemberType(member), raw);
                    row[start + i] = new GameDataField { Value = text, Modified = true };
                }

                return row;
            }
        }

        public void Download(GameDataField[][] formattedData, GameDataSyncState state)
        {
            this.State = state;
            var mapping = BuildTypeMap(formattedData.Take(2).ToArray());
            if (mapping == null) return;
            try
            {
                mapping.ParseData(formattedData.Skip(2).ToArray(), state);
            }
            catch (Exception ex)
            {
                throw new Exception(mapping.Name, ex);
            }
        }

        public void Upload(ref GameDataField[][] formattedData, GameDataSyncState state)
        {
            this.State = state;
            var mapping = BuildTypeMap(formattedData.Take(2).ToArray());
            if (mapping == null) return;

            mapping.ParseData(formattedData.Skip(2).ToArray(), state);

            var rows = formattedData.ToList();
            var colCount = formattedData[0].Length;

            foreach (var so in state.Prefabs.GetSOs(mapping.ObjectType))
            {
                string name = (so as UnityEngine.Object)?.name ?? so.ToString();
                var nameField = new GameDataField(name);
                if (mapping.CheckIfObjectExists(nameField, state, so, out _))
                    continue;
                rows.Add(mapping.CreateRow(so, state, colCount));
            }

            // return
            formattedData = rows.ToArray();
        }

        private FieldMapping BuildTypeMap(GameDataField[][] headers)
        {
            var baseTypes = headers[0];
            var fieldNames = headers[1];
            var previousHeader = -1;
            FieldMapping result = null;

            for (var i = 0; i < fieldNames.Length; i++)
            {
                if (i < baseTypes.Length && !baseTypes[i].IsEmpty)
                    previousHeader = i;

                if (previousHeader < 0) continue;

                var boundary = i + 1 >= fieldNames.Length
                            || fieldNames[i + 1].IsEmpty
                            || (i + 1 < baseTypes.Length && !baseTypes[i + 1].IsEmpty);

                if (!boundary) continue;

                if (!AddFieldMapping(ref result,
                                     baseTypes[previousHeader],
                                     previousHeader,
                                     i,
                                     fieldNames))
                    return null;

                previousHeader = -1;
            }

            return result;
        }

        private bool AddFieldMapping(
            ref FieldMapping mapping,
            GameDataField baseName,
            int start,
            int end,
            GameDataField[] fieldNames
        )
        {
            var parts = baseName.Value.Split(':');
            var typeName = parts[0].Trim();
            var currentType = State.GetType(typeName);
            if (currentType == null) return false;

            var memberInfos = fieldNames
                .Skip(start)
                .Take(end + 1 - start)
                .Select(c => GetMemberFromName(currentType, c.Value))
                .ToArray();

            if (parts.Length == 1)
            {
                // top‑level
                mapping = new FieldMapping(currentType, memberInfos, start);
            }
            else
            {
                // child: "Parent:ChildType.ChildField"
                var childParts = parts[1].Split('.');
                var parentType = State.GetType(childParts[0].Trim());
                var childMember = GetMemberFromName(parentType, childParts[1].Trim());
                var parentMap = mapping.FindWithType(parentType);
                parentMap.AddChild(childMember, new FieldMapping(currentType, memberInfos, start));
            }

            return true;
        }

        private static MemberInfo GetMemberFromName(Type type, string name)
        {
            name = name.Replace(" ", "");
            var flags = BindingFlags.Instance
                      | BindingFlags.Public
                      | BindingFlags.NonPublic
                      | BindingFlags.IgnoreCase;

            var f = type.GetField(name, flags);
            if (f != null) return f;
            var p = type.GetProperty(name, flags & ~BindingFlags.IgnoreCase);
            if (p != null) return p;
            Debug.LogError($"Cannot find field or property '{name}' on {type}");
            return null;
        }

        public static Type GetMemberType(MemberInfo m)
            => m is FieldInfo fi ? fi.FieldType
             : m is PropertyInfo pi ? pi.PropertyType
             : throw new ArgumentException("Not a field or property");

        public static object GetMemberValue(MemberInfo m, object obj)
            => m is FieldInfo fi ? fi.GetValue(obj)
             : m is PropertyInfo pi ? pi.GetValue(obj)
             : null;

        public static void SetMemberValue(MemberInfo m, object obj, object value)
        {
            switch (m)
            {
                case FieldInfo fi:
                    fi.SetValue(obj, value);
                    break;
                case PropertyInfo pi:
                    pi.SetValue(obj, value);
                    break;
            }
        }
    }
}
