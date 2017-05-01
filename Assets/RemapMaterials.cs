using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEngine;

public class RemapMaterials : EditorWindow
{
    // Add menu item named "My Window" to the Window menu
    [MenuItem("Remap Materials/Remap")]
    static void RemapMaterialsInObject()
    {
        _obj = Selection.activeObject as GameObject;
        ResetMapping();

        //Show existing window instance. If one doesn't exist, make one.
        EditorWindow.GetWindow(typeof(RemapMaterials));
    }

    private static List<MaterialWrapper> _materialsInObjectDict = new List<MaterialWrapper>();
    private static List<MaterialWrapper> _materialsInProjectList = new List<MaterialWrapper>();

    private static string[] _materialsInObject;
    private static string[] _materialsInProject;

    private static int[] _selectedIndices;

    private static GameObject _obj;
    private Vector2 _scroll = Vector2.zero;

    private static Dictionary<string, string> Mapping = new Dictionary<string, string>();
 
    void OnGUI()
    {
        GUILayout.Label("Model Material Mapping", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.BeginVertical();

        int index = 0;
        foreach (var material in _materialsInObject)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(material);

            _selectedIndices[index] = EditorGUILayout.Popup("", _selectedIndices[index], _materialsInProject);
            index++;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
        {
            SaveMapping();
        }
        if (GUILayout.Button("Load"))
        {
            LoadMapping();
        }
        if (GUILayout.Button("Reset"))
        {
            ResetMapping();
        }

        if (GUILayout.Button("Apply Mapping"))
        {
            var map = GenerateMapping();
            ApplyMapping(_obj, map);
        }
        EditorGUILayout.EndHorizontal();
    }

    private static void ResetMapping()
    {
        _materialsInObjectDict = GetUniqueMaterials(_obj);
        _materialsInObject = new string[_materialsInObjectDict.Count];
        int index = 0;
        foreach (var mat in _materialsInObjectDict)
        {
            _materialsInObject[index++] = mat.material.name;
        }

        var guids = AssetDatabase.FindAssets("t:Material", null);
        _materialsInProject = new string[guids.Length];

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = (Material)AssetDatabase.LoadAssetAtPath(path, typeof(Material));

            if (_materialsInProjectList.FindIndex(x => x.guid == guid) == -1)
                _materialsInProjectList.Add(new MaterialWrapper() { material = mat, guid = guid });
        }

        index = 0;
        foreach (var mat in _materialsInProjectList)
        {
            _materialsInProject[index++] = mat.material.name;
        }

        // Need a selected index for each material in source object:
        _selectedIndices = new int[_materialsInObjectDict.Count];

        index = 0;
        foreach (var mt in _materialsInObjectDict)
        {
            // find material by guid in the materials in project list
            var idx = _materialsInProjectList.FindIndex(x => x.guid == mt.guid);
            _selectedIndices[index++] = idx;
        }
    }

    private static Dictionary<string, string> GenerateMapping()
    {
        Dictionary<string, string> mapping = new Dictionary<string, string>();

        int index = 0;
        foreach (var mat in _materialsInObjectDict)
        {
            int idx = _selectedIndices[index++];

            // This index indexes into the project materials..
            var projMat = _materialsInProjectList[idx];
            //if (mat.guid == projMat.guid)
            //    continue;
            mapping[mat.guid] = projMat.guid;
        }
        return mapping;
    }

    private static void SaveMapping()
    {
        var path = EditorUtility.SaveFilePanel("Save mapping", "", _obj.name + ".mapping", "mapping");
        if (path.Length != 0)
        {
            var dict = GenerateMapping();

            var bf = new BinaryFormatter();
            using (var file = new FileStream(path, FileMode.OpenOrCreate))
            {
                bf.Serialize(file, dict);
            }
        }
    }

    private static void LoadMapping()
    {
        var pathname = EditorUtility.OpenFilePanelWithFilters("Load mapping", "", new string[] { "mapping", "mapping" });
        using (var file = new FileStream(pathname, FileMode.Open))
        {
            var bf = new BinaryFormatter();
            var dict = (Dictionary<string, string>)bf.Deserialize(file);

            // Go through each guid mapping
            foreach (var map in dict)
            {
                // If we can find the source material still on our object..
                int idx = _materialsInObjectDict.FindIndex(m => m.guid == map.Key);
                if (idx != -1)
                {
                    int idxDst = _materialsInProjectList.FindIndex(m => m.guid == map.Value);
                    if (idxDst != -1)
                    {
                        _selectedIndices[idx] = idxDst;
                    }
                    else
                    {
                        Debug.LogWarning(string.Format("Material {0} Not found in project", map.Key));
                    }
                }
                else
                {
                    Debug.LogWarning(string.Format("Material {0} Not found on object", map.Key));
                }
            }
        }
    }

    private static void ApplyMapping(GameObject obj, Dictionary<string, string> map)
    {
        ForeachMaterialRecursive(obj, (m, g, mr) =>
        {
            if (!map.ContainsKey(g))
                return false;

            ReplaceMaterial(mr, g, map[g]);

            return true;
        });
    }

    static string GetGuid(Material mat)
    {
        var assetPath = AssetDatabase.GetAssetPath(mat.GetInstanceID());
        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        return guid;
    }

    private static void ReplaceMaterial(MeshRenderer renderer, string guidSrc, string guidDst)
    {
        if (string.IsNullOrEmpty(guidSrc) || string.IsNullOrEmpty(guidDst) || guidSrc == guidDst)
            return;

        var materials = renderer.sharedMaterials;
        for (int i=0;i<materials.Length;i++)
        {
            // Get the guid of the material and compare against the map...
            var guid = GetGuid(materials[i]);
            if (guid == guidSrc)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guidDst);

                // find project material by guid..
                int idx = _materialsInProjectList.FindIndex(m => m.guid == guidDst);
                var mat = _materialsInProjectList[idx];

                if (materials.Length == 1)
                {
                    renderer.sharedMaterial = mat.material;
                }
                else
                {
                    // Need to set the whole array here...
                    Material[] copy = new Material[renderer.sharedMaterials.Length];
                    Array.Copy(renderer.sharedMaterials, copy, renderer.sharedMaterials.Length);
                    copy[i] = mat.material;
                    renderer.sharedMaterials = copy;
                }
                return;
            }
        }
    }

    struct MaterialWrapper
    {
        public Material material;
        public string guid;
    }

    private static List<MaterialWrapper> GetUniqueMaterials(GameObject obj)
    {
        List<MaterialWrapper> mats = new List<MaterialWrapper>();
        FindMaterialsRecursive(obj, mats);
        return mats;
    }

    private static void ForeachMaterial(GameObject obj, Func<Material, string, MeshRenderer, bool> callback)
    {
        var meshRenderer = obj.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            var matList = meshRenderer.sharedMaterials;
            foreach (var mat in matList)
            {
                var shader = mat.shader;
                var name = shader.name;
                var instId = shader.GetInstanceID();
                Debug.Log(string.Format("material - {0} {1}", mat.name, mat.GetInstanceID()));
                Debug.Log(string.Format("name = {0} - {1}", name, instId));

                // Need to look itself up in the project materials list to get the current index..
                var assetPath = AssetDatabase.GetAssetPath(mat.GetInstanceID());
                var guid = AssetDatabase.AssetPathToGUID(assetPath);

                callback(mat, guid, meshRenderer);
            }
        }
    }
    private static void ForeachMaterialRecursive(GameObject obj, Func<Material, string, MeshRenderer, bool> callback)
    {
        ForeachMaterial(obj, callback);
        var children = obj.GetComponentsInChildren<Transform>(true);
        foreach (var child in children)
        {
            ForeachMaterial(child.gameObject, callback);
        }
    }

    private static void FindMaterialsRecursive(GameObject obj, List<MaterialWrapper> materials)
    {
        Debug.Log(string.Format("Finding materials for {0}", obj.name));

        ForeachMaterialRecursive(obj, (m, g, mr) =>
        {
            if ((materials.FindIndex(mw => mw.guid == g)) == -1)
            {
                materials.Add(new MaterialWrapper() { material = m, guid = g });
            }
            return true;
        });
    }

    // Validate the menu item defined by the function above.
    // The menu item will be disabled if this function returns false.
    [MenuItem("Remap Materials/Remap", true)]
    static bool ValidateRemapMaterialsInObject()
    {
        // Return false if no transform is selected.
        return Selection.activeObject as GameObject != null;
    }
}

