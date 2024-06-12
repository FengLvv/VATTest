using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UIElements;



public class BubbleControl : MonoBehaviour {
    public float currentFrame=181;
    public MeshRenderer fluidRenderer;
    public MeshRenderer bubbleRenderer;
    PlayableDirector _pd;
    Material _fluidMat;
    Material _bubbleMat;
    readonly static int DisplayFrame = Shader.PropertyToID( "_DisplayFrame" );
    
    void InitData()
    {
        if( _fluidMat == null || _bubbleMat == null) {
            _fluidMat = fluidRenderer.material;
            _bubbleMat = bubbleRenderer.material;
        }
    }
    
    
    
    void Update()
    {
        InitData();
        _fluidMat.SetFloat(DisplayFrame, currentFrame);
        _bubbleMat.SetFloat(DisplayFrame, currentFrame);
        
    }
}
