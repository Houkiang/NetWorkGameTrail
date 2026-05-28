using UnityEngine;

[DisallowMultipleComponent]
public class MinimapRenderable : MonoBehaviour
{
    [SerializeField]
    private bool includeChildMeshFilters = true;

    public bool IncludeChildMeshFilters => includeChildMeshFilters;

    public MeshFilter[] GetTargetMeshFilters()
    {
        return includeChildMeshFilters
            ? GetComponentsInChildren<MeshFilter>(true)
            : GetComponents<MeshFilter>();
    }
}
