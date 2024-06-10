using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class CreateSDF : EditorWindow {
	[MenuItem( "CustomTools/SDF" )]
	static void OpenWindows() {
		GetWindow<CreateSDF>( false, "CreateSDF", true ).Show();
	}

	int _width;
	int _height;
	float _scaleDown = 0;

	ComputeShader _computeSDF;
	RenderTexture _tempTexture;
	RenderTexture _sdfTexture;
	Texture2D[] _oriTextures;

	readonly static int OutputTex = Shader.PropertyToID( "_OutputTex" );
	readonly static int TempTex = Shader.PropertyToID( "_TempTex" );
	readonly static int OriginalTex = Shader.PropertyToID( "_OriginalTex" );
	readonly static int TexSize = Shader.PropertyToID( "_TexSize" );
	void OnGUI() {
		_computeSDF = Resources.Load<ComputeShader>( "ComputeSDF" );

		EditorGUILayout.LabelField( "请选择所有 SDF 图的 texture2d" );
		Object[] selectObjs = Selection.objects;
		_oriTextures = selectObjs.Where( x => x is Texture2D ).Cast<Texture2D>().ToArray();
		_width = _oriTextures.FirstOrDefault()?.width ?? 0;
		_height = _oriTextures.FirstOrDefault()?.height ?? 0;

		//绘制 Gui 
		EditorGUILayout.BeginVertical(); //开启垂直视图绘制
		EditorGUILayout.LabelField( "选择的文件为: " + _oriTextures.FirstOrDefault()?.name + "等" + _oriTextures.Length + "个文件" ); //文本

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "选择黑白图片，转化为 SDF 图，可多选" ); //文本
		_scaleDown = EditorGUILayout.Slider( label:"缩放系数（越大则渐变越明显） ", _scaleDown, 1, 1000 ); //滑动条

		if( GUILayout.Button( "转化为 SDF" ) ) {
			Create( _oriTextures ).Forget();
		}

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "选择所有 SDF 图，转化为单张渐变图（SDF需要以数字结尾，结尾数字代表当前图插值位置）" ); //文本
		EditorGUILayout.LabelField( "例如五张图的面部sdf，结尾就是0，45，90，135，180" ); //文本
		if( GUILayout.Button( "合成单张SDF" ) ) {
			ComposeSDF().Forget();
		}

		EditorGUILayout.EndVertical();
	}

	async UniTask InitTexture() {
		_sdfTexture = new RenderTexture( _width, _height, 0, RenderTextureFormat.RFloat ){
			enableRandomWrite = true
		};
		_sdfTexture.Create();

		_tempTexture = new RenderTexture( _width, _height, 0, RenderTextureFormat.RFloat ){
			enableRandomWrite = true
		};
		_tempTexture.Create();

		await UniTask.WaitUntil( () => _sdfTexture.IsCreated() && _tempTexture.IsCreated() );
	}

	async UniTaskVoid ComposeSDF() {
		await InitTexture();

		// 将 SDF 编号和纹理对应
		Dictionary<int, Texture2D> numRT = _oriTextures.ToDictionary( ( x ) => int.Parse( x.name.Split( '_' ).Last() ), ( x ) => x );

		// 取出编号
		var keys = numRT.Keys.ToList();
		// 当前使用的 sdf 图
		int keyIdx = 1;

		// 最后一个 sdf 图的编号就是最大值
		float sdfMax = keys.Last();
		// 每一个深度对应的 sdf 比率
		float ratio = ( sdfMax ) / 255f;

		// 每个深度叠加合成图的亮度
		for( int i = 0; i < 256; i++ ) {
			// 当前深度的比率大于 sdf 图的编号
			if( ratio * i > keys[keyIdx] ) {
				if( keyIdx < keys.Count - 1 ) {
					keyIdx++;
				}
			}
			float weight = ( ratio * i - keys[keyIdx - 1] ) / ( keys[keyIdx] - keys[keyIdx - 1] );
			ComputeComposeSDF( weight, numRT[keys[keyIdx - 1]], numRT[keys[keyIdx]], _sdfTexture );
		}
		SaveTexture( _sdfTexture, _oriTextures.FirstOrDefault() );
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	void ComputeComposeSDF( float weight, Texture2D sdf1, Texture2D sdf2, RenderTexture outTexture ) {
		var cmd = new CommandBuffer();
		cmd.SetComputeFloatParam( _computeSDF, "_Weight", weight );
		cmd.SetComputeTextureParam( _computeSDF, 4, "_SDF1", sdf1 );
		cmd.SetComputeTextureParam( _computeSDF, 4, "_SDF2", sdf2 );
		cmd.SetComputeTextureParam( _computeSDF, 4, "_OutputTex", outTexture );
		cmd.DispatchCompute( _computeSDF, 4, _width / 32, _height / 32, 1 );
		Graphics.ExecuteCommandBuffer( cmd );
	}


	/// <summary>
	/// 为所有输入的纹理生成SDF
	/// </summary>
	/// <param name="texture"></param>
	async UniTaskVoid Create( Texture2D[] texture ) {
		// 创建初始纹理，等待创建完成
		await InitTexture();

		foreach( Texture2D t in texture ) {
			// 为 t 生成 SDF，保存到 _sdfTexture
			GenerateSDF( t, _sdfTexture );

			// 保存 _sdfTexture 为 png
			SaveTexture( _sdfTexture, t );
		}
	}


	/// <summary>
	/// 生成一张 SD F纹理
	/// </summary>
	/// <param name="texture">用来生成的纹理</param>
	/// <param name="outTexture">生成后保存到这里</param>
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

	/// <summary>
	/// 保存 RT 为 png
	/// </summary>
	/// <param name="texture">保存的RT</param>
	/// <param name="oriTex">原始图片，只用来计算图片存储位置</param>
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

		// 将文件名和末尾数字拆开
		string num = s.Split( '_' ).Last();
		string picName = s.Substring( 0, s.Length - num.Length - 1 );

		s = dir + "/" + picName + "_SDF_" + num + ".png";

		SaveRTToFile( texture, s );
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	/// <summary>
	///  保存 rt 为 png
	/// </summary>
	/// <param name="rt"></param>
	/// <param name="path"></param>
	static void SaveRTToFile( RenderTexture rt, string path ) {
		RenderTexture.active = rt;
		Texture2D tex = new Texture2D( rt.width, rt.height, TextureFormat.RGB24, false );
		tex.ReadPixels( new Rect( 0, 0, rt.width, rt.height ), 0, 0 );
		RenderTexture.active = null;

		byte[] bytes = tex.EncodeToPNG();

		File.WriteAllBytes( path, bytes );
		AssetDatabase.ImportAsset( path );
	}


}