﻿
public class RemoteResourceProvider : IResourceProvider
{
    public float LoadProgress { get { throw new System.NotImplementedException(); } }

    public void LoadResourceAsync<T> (string path) where T : UnityEngine.Object
    {
        throw new System.NotImplementedException();
    }

    public void UnloadResourceAsync (string path)
    {
        throw new System.NotImplementedException();
    }

    public T GetResource<T> (string path) where T : UnityEngine.Object
    {
        throw new System.NotImplementedException();
    }
}