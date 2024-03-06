using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using GaussianSplatting.Runtime;
using UnityEngine.Assertions;

public class PlyManager : MonoBehaviour
{
    // string m_PrevPlyPath;
    private FileSystemWatcher watcher; 
    public static GaussianSplatRenderer renderer;
    public GameObject aaaaa;    
    public bool m_hasLoaded = false;
    const string m_InputFile = "GaussianAssets/output";
    
    // Start is called before the first frame update
    void Start()
    {
            renderer = this.gameObject.GetComponent<GaussianSplatRenderer>();        
    }

    //TODO  使用生成的button来调用这个函数
    private void BindGSAsset(){
        if (renderer == null) {
            Debug.LogError("No GaussianSplatRenderer object found");
            return;
        }
        GaussianSplatAsset a = Resources.Load<GaussianSplatAsset>(m_InputFile);
        renderer.m_Asset=a;
    }
    // Update is called once per frame
    void Update()
    {   
        if(m_hasLoaded == false && Resources.Load<GaussianSplatAsset>(m_InputFile) != null)
        {
            BindGSAsset();
            m_hasLoaded = true;
        }
    }
}
