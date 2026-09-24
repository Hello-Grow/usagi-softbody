using MeshProcess;
using UnityEditor;
using UnityEngine;

public static class VHACDConvexColliderMenu
{
    private const string GeneratedAssetFolder = "Assets/Generated/VHACD";
    private const string GeneratedHolderName = "Generated Convex Colliders";

    [MenuItem("CONTEXT/VHACD/Generate Convex Colliders")]
    private static void GenerateConvexColliders(MenuCommand command)
    {
        var vhacd = (VHACD)command.context;
        var meshFilter = vhacd.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("VHACD needs a MeshFilter with a mesh before it can generate colliders.", vhacd);
            return;
        }

        EnsureAssetFolder();
        RemovePreviousColliders(vhacd.transform);

        var holder = new GameObject(GeneratedHolderName);
        Undo.RegisterCreatedObjectUndo(holder, "Generate convex colliders");
        holder.transform.SetParent(vhacd.transform, false);

        var hulls = vhacd.GenerateConvexMeshes(meshFilter.sharedMesh);
        for (var index = 0; index < hulls.Count; index++)
        {
            var hull = hulls[index];
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{GeneratedAssetFolder}/{vhacd.gameObject.name}_Hull_{index + 1}.asset");
            AssetDatabase.CreateAsset(hull, assetPath);

            var hullObject = new GameObject($"Convex Hull {index + 1}");
            Undo.RegisterCreatedObjectUndo(hullObject, "Generate convex colliders");
            hullObject.transform.SetParent(holder.transform, false);

            var collider = hullObject.AddComponent<MeshCollider>();
            collider.sharedMesh = hull;
            collider.convex = true;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Generated {hulls.Count} convex colliders for {vhacd.name}.", vhacd);
    }

    private static void EnsureAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated"))
            AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(GeneratedAssetFolder))
            AssetDatabase.CreateFolder("Assets/Generated", "VHACD");
    }

    private static void RemovePreviousColliders(Transform root)
    {
        var previousHolder = root.Find(GeneratedHolderName);
        if (previousHolder != null)
            Undo.DestroyObjectImmediate(previousHolder.gameObject);
    }
}
