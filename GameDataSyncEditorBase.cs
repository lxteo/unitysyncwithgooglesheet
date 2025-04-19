namespace OCG
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    [CustomEditor(typeof(GameDataSync), true)]
    public abstract class GameDataSyncEditorBase : Editor
    {
        public abstract Type GetTypeFunc(string typeName);
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var myScript = (GameDataSync)target;
            if (GUILayout.Button("Download"))
            {

                DownloadData(myScript, GetTypeFunc);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            if (GUILayout.Button("Upload"))
            {
                UploadData(myScript, GetTypeFunc);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }

        public static void DownloadData(GameDataSync go, Func<string, Type> getTypeFunc)
        {
            Undo.RecordObject(go.Prefabs, "Clear objects.");
            go.Prefabs.ClearCache();
            Clear(go.Prefabs, getTypeFunc);
            var data = new GameDataSheet[go.Prefabs.ScriptableObjectsToSync.Count];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = new GameDataSheet();
            }

            var download = GoogleSheetsWrapper.GetData(go.Ranges);
            if (download != null)
            {
                for (var i = 0; i < download.Count; i++)
                {
                    var formattedData = GetFixedLengthStringArrays(download[i]);
                    var doneObjects = new Dictionary<string, object>();
                    var state = new GameDataSyncState()
                    {
                        Mode = GameDataSyncMode.Download,
                        DoneObjects = doneObjects,
                        GetOrCreateObjectMethod = PrefabAndSOHelper.GetOrCreateSOInEditor,
                        Prefabs = go.Prefabs,
                        GetTypeFunc = getTypeFunc,
                    };
                    data[i].Download(formattedData, state);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            go.Prefabs.OnFinishedDownload();
        }

        public static void UploadData(GameDataSync go, Func<string, Type> getTypeFunc)
        {
            Undo.RecordObject(go.Prefabs, "Clear objects.");
            go.Prefabs.ClearCache();
            Clear(go.Prefabs, getTypeFunc);
            var data = new GameDataSheet[go.Prefabs.ScriptableObjectsToSync.Count];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = new GameDataSheet();
            }
            var download = GoogleSheetsWrapper.GetData(go.Ranges);
            if (download != null)
            {
                for (var i = 0; i < download.Count; i++)
                {
                    var doneObjects = new Dictionary<string, object>();
                    var state = new GameDataSyncState()
                    {
                        Mode = GameDataSyncMode.Upload,
                        DoneObjects = doneObjects,
                        GetOrCreateObjectMethod = PrefabAndSOHelper.GetOrCreateSOInEditor,
                        Prefabs = go.Prefabs,
                        GetTypeFunc = getTypeFunc,
                    };
                    var formattedData = GetFixedLengthStringArrays(download[i]);
                    data[i].Upload(ref formattedData, state);
                    download[i] = formattedData.Select(c => c.Select(c => c.Value).ToArray()).ToArray();
                    //UpdateFromGameDataFields(formattedData, download[i]);
                }
                GoogleSheetsWrapper.UpdateData(go.Ranges, download);
            }
        }

        private static void Clear(SerializedObjectPrefabs soPrefabs, Func<string, Type> getTypeFunc)
        {
            foreach (var s in soPrefabs.ScriptableObjectsToSync)
            {
                Undo.RecordObject(soPrefabs, "Added SO.");
                PrefabAndSOHelper.GetSOsFromFile(s, soPrefabs.objects, getTypeFunc);
            }
            foreach (var s in soPrefabs.OtherFolders)
            {
                Undo.RecordObject(soPrefabs, "Added SO.");
                PrefabAndSOHelper.GetSOsFromFile(s, soPrefabs.objects, getTypeFunc);
            }
            EditorUtility.SetDirty(soPrefabs);
        }

        private static GameDataField[][] GetFixedLengthStringArrays(IList<IList<object>> data)
        {
            var length = data.Skip(1).First().Count();
            return data.Select(c => ToFixedLengthArray(c, length)).ToArray();
        }


        private static GameDataField[] ToFixedLengthArray(IList<object> c, int length)
        {
            var result = new GameDataField[length];
            for (var i = 0; i < result.Length; i++)
            {
                if (c.Count <= i)
                {
                    result[i] = new GameDataField(null);
                }
                else
                {
                    result[i] = new GameDataField(c[i]);
                }
            }
            return result;
        }
    }
}
