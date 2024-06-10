using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public class CreateSDF : EditorWindow {
	[MenuItem( "CustomTools/SDF" )]
	static void OpenWindows() {
		GetWindow<CreateSDF>( false, "CreateSDF", true ).Show();
	}

	float _scaleDown = 0;
	float _sdfMax = 180;
	string max = "180";

	// Statue Vals
	ComputeShader _computeSDF;
	RenderTexture _tempTexture;
	RenderTexture _tempTexture2;
	RenderTexture[] _sdfTexture;

	readonly static int OutputTex = Shader.PropertyToID( "_OutputTex" );
	readonly static int TempTex = Shader.PropertyToID( "_TempTex" );
	readonly static int OriginalTex = Shader.PropertyToID( "_OriginalTex" );
	readonly static int TexSize = Shader.PropertyToID( "_TexSize" );
	void OnGUI() {
		_computeSDF = Resources.Load<ComputeShader>( "ComputeSDF" );

		EditorGUILayout.LabelField( "请选择所有 SDF 图的 texture2d" );
		Object[] selectObjs = Selection.objects;
		Texture2D[] textures = selectObjs.Where( x => x is Texture2D ).Cast<Texture2D>().ToArray();

		//绘制 Gui 
		EditorGUILayout.BeginVertical(); //开启垂直视图绘制
		EditorGUILayout.LabelField( "选择的文件为: " + textures.FirstOrDefault()?.name + "等" ); //文本

		EditorGUILayout.LabelField( "缩放系数（越大则渐变越明显）:" ); //文本
		_scaleDown = EditorGUILayout.Slider( label:"缩放系数", _scaleDown, 100, 1000 ); //滑动条

		if( GUILayout.Button( "转化为 SDF" ) ) {
			Create( textures ).Forget();
		}

		EditorGUILayout.LabelField( "sdf图最大值" ); //文本
		max = GUILayout.TextField( max );

		if( GUILayout.Button( "合成单张SDF" ) ) {
			_sdfMax = float.Parse( max );

			Object[] s = Selection.objects;
			_sdfTexture = s.Where( x => x is RenderTexture ).Cast<RenderTexture>().ToArray();
			if( _sdfTexture is null || _sdfTexture.Length == 0 ) {
				EditorGUILayout.LabelField( "先选择所有SDF" );
			}
			ComposeSDF( _sdfTexture[0] );
		}


		EditorGUILayout.EndVertical();
	}

	void ComposeSDF( RenderTexture texturePos ) {
		int width = _sdfTexture[0].width;
		int height = _sdfTexture[0].height;
		var sdfOUT = RenderTexture.GetTemporary( width, height, 0, RenderTextureFormat.RFloat );
		sdfOUT.enableRandomWrite = true;
		sdfOUT.hideFlags = HideFlags.None;
		sdfOUT.Create();

		SaveTexture( sdfOUT, texturePos );
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Dictionary<int, RenderTexture> numRT = new Dictionary<int, RenderTexture>();
		numRT = _sdfTexture.ToDictionary( x => {
			string[] parts = x.name.Split( '_' );
			int secondToLastPart = int.Parse( parts[^2] );
			return secondToLastPart;
		}, x => x );
		var keys = numRT.Keys.ToList();
		int keyIdx = 1;
		float ratio = ( _sdfMax ) / 255f;
		for( int i = 0; i < 256; i++ ) {
			if( ratio * i > keys[keyIdx] ) {
				if( keyIdx < keys.Count - 1 ) {
					keyIdx++;
				}
			}
			float weight = ( ratio * i - keys[keyIdx - 1] ) / ( keys[keyIdx] - keys[keyIdx - 1] );
			Debug.Log( weight );
			ComposeSDFShader( weight, numRT[keys[keyIdx - 1]], numRT[keys[keyIdx]], sdfOUT );
		}
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	void ComposeSDFShader( float weight, RenderTexture RT1, RenderTexture RT2, RenderTexture outTexture ) {
		var cmd = new CommandBuffer();
		cmd.SetComputeFloatParam( _computeSDF, "_Weight", weight );
		cmd.SetComputeTextureParam( _computeSDF, 4, "_SDF1", RT1 );
		cmd.SetComputeTextureParam( _computeSDF, 4, "_SDF2", RT2 );
		cmd.SetComputeTextureParam( _computeSDF, 4, "_OutputTex", outTexture );
		cmd.DispatchCompute( _computeSDF, 4, RT1.width / 32, RT1.height / 32, 1 );
		Graphics.ExecuteCommandBuffer( cmd );
	}

	async UniTaskVoid Create( Texture2D[] texture ) {
		int width = texture[0].width;
		int height = texture[0].height;
		_tempTexture = RenderTexture.GetTemporary( width, height, 0, RenderTextureFormat.RFloat );
		_tempTexture.enableRandomWrite = true;
		_tempTexture.Create();

		_sdfTexture = new RenderTexture[texture.Length];
		for( int index = 0; index < _sdfTexture.Length; index++ ) {
			_sdfTexture[index] = new RenderTexture( width, height, 0, RenderTextureFormat.R8 ){
				enableRandomWrite = true
			};
			_sdfTexture[index].Create();
			await UniTask.WaitUntil(()=>_sdfTexture[index].IsCreated());
		}

		Debug.Log( "create assets" );
		await UniTask.WaitForSeconds( 1f );

		for( int index = 0; index < _sdfTexture.Length; index++ ) {
			Texture2D t = texture[index];

			GenerateSDF( t, _sdfTexture[index] );

			// await UniTask.WaitForSeconds( 0.5f );

			Debug.Log( "生成第" + ( index + 1 ) + "张图片" );
			SaveTexture( _sdfTexture[index], texture[index] );
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
		
		await UniTask.WaitForSeconds( 1f );
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}
	
	void GenerateSDF( Texture2D texture, RenderTexture outTexture ) {
		var cmd = new CommandBuffer();

		// 创建临时纹理: 3D纹理, 开启写入，不隐藏
		int textureWidth = texture.width;
		int textureHeight = texture.height;

		cmd.SetComputeFloatParam( _computeSDF, "_ScaleDown", _scaleDown );
		cmd.SetComputeVectorParam( _computeSDF, TexSize, new Vector2( textureWidth, textureHeight ) );

		cmd.SetComputeTextureParam( _computeSDF, 0, OriginalTex, texture );
		cmd.SetComputeTextureParam( _computeSDF, 0, TempTex, _tempTexture );
		cmd.DispatchCompute( _computeSDF, 0, textureWidth / 32, textureHeight / 32, 1 );

		cmd.SetComputeTextureParam( _computeSDF, 1, TempTex, _tempTexture );
		cmd.SetComputeTextureParam( _computeSDF, 1, OutputTex, outTexture );
		cmd.DispatchCompute( _computeSDF, 1, textureWidth / 32, textureHeight / 32, 1 );


		cmd.SetComputeTextureParam( _computeSDF, 2, OriginalTex, texture );
		cmd.SetComputeTextureParam( _computeSDF, 2, TempTex, _tempTexture );
		cmd.DispatchCompute( _computeSDF, 2, textureWidth / 32, textureHeight / 32, 1 );

		cmd.SetComputeTextureParam( _computeSDF, 3, TempTex, _tempTexture );
		cmd.SetComputeTextureParam( _computeSDF, 3, OutputTex, outTexture );
		cmd.DispatchCompute( _computeSDF, 3, textureWidth / 32, textureHeight / 32, 1 );


		Graphics.ExecuteCommandBuffer( cmd );
		cmd.Release();
	}

	void SaveTexture( RenderTexture texture, Texture2D oriTex ) {
		// create folder
		string path = AssetDatabase.GetAssetPath( oriTex );
		// 移除格式
		int index = path.LastIndexOf( ".", StringComparison.Ordinal );
		path = path.Remove( index );
		// 拆分为文件夹和文件名
		var c = path.LastIndexOf( "/", StringComparison.Ordinal );
		string dir = path.Remove( c ) + "/SDF";
		string s = path.Substring( c + 1 );
		// 创建文件夹
		if( Directory.Exists( dir ) == false ) {
			Directory.CreateDirectory( dir );
		}

		s = dir + "/" + s + "_SDF" + ".png";
		// Create a new asset at the specified path
		SaveRTToFile( texture, s );
		// AssetDatabase.CreateAsset( texture, s );
	}

		public static void SaveRTToFile(RenderTexture rt,string path)
	{
		RenderTexture.active = rt;
		Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		RenderTexture.active = null;

		byte[] bytes;
		bytes = tex.EncodeToPNG();
        
		System.IO.File.WriteAllBytes(path, bytes);
		AssetDatabase.ImportAsset(path);
		Debug.Log("Saved to " + path);
	}
	
	
	
	void SaveTexture( RenderTexture texture, RenderTexture oriTex ) {
		// create folder
		string path = AssetDatabase.GetAssetPath( oriTex );
		// 移除格式
		int index = path.LastIndexOf( ".", StringComparison.Ordinal );
		path = path.Remove( index );
		// 拆分为文件夹和文件名
		var c = path.LastIndexOf( "/", StringComparison.Ordinal );
		string dir = path.Remove( c ) + "/SDF";
		string s = path.Substring( c + 1 );
		// 创建文件夹
		if( Directory.Exists( dir ) == false ) {
			Directory.CreateDirectory( dir );
		}

		s = dir + "/" + s + "_SDF" + ".renderTexture";
		// Create a new asset at the specified path
		AssetDatabase.CreateAsset( texture, s );
	}

	void OnDestroy() {
		//清理资源
		if( _tempTexture != null ) {
			RenderTexture.ReleaseTemporary( _tempTexture );
		}
	}
}