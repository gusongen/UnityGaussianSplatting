using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[CustomEditor(typeof(GaussianSplatRenderer))]
[BurstCompile]
public class GaussianSplatRendererEditor : Editor
{
    const int kRowHeight = 12;
    static string[] kFieldNames = {
        "px", "py", "pz", // 0
        "nx", "ny", "nz", // 3
        "dc0r", "dc0g", "dc0b", // 6
        "sh0r", "sh0g", "sh0b", // 9
        "sh1r", "sh1g", "sh1b", // 12
        "sh2r", "sh2g", "sh2b", // 15
        "sh3r", "sh3g", "sh3b", // 18
        "sh4r", "sh4g", "sh4b", // 21
        "sh5r", "sh5g", "sh5b", // 24
        "sh6r", "sh6g", "sh6b", // 27
        "sh7r", "sh7g", "sh7b", // 30
        "sh8r", "sh8g", "sh8b", // 33
        "sh9r", "sh9g", "sh9b", // 36
        "shAr", "shAg", "shAb", // 39
        "shBr", "shBg", "shBb", // 42
        "shCr", "shCg", "shCb", // 45
        "shDr", "shDg", "shDb", // 48
        "shEr", "shEg", "shEb", // 51
        "op", // 54
        "sx", "sy", "sz", // 55
        "rw", "rx", "ry", "rz", // 58
    };

    Vector2[] m_CachedDataRanges;
    Texture2D m_StatsTexture;
    int m_CameraIndex = 0;

    public void OnDestroy()
    {
        if (m_StatsTexture) DestroyImmediate(m_StatsTexture);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gs = target as GaussianSplatRenderer;
        if (!gs)
            return;

        EditorGUILayout.Space();

        var cameras = gs.cameras;
        if (cameras != null && cameras.Length != 0)
        {
            var camIndex = EditorGUILayout.IntSlider("Camera", m_CameraIndex, 0, cameras.Length - 1);
            camIndex = math.clamp(camIndex, 0, cameras.Length - 1);
            if (camIndex != m_CameraIndex)
            {
                m_CameraIndex = camIndex;

                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    var cam = cameras[camIndex];
                    mainCam.transform.position = cam.pos;
                    mainCam.transform.LookAt(cam.pos + cam.axisZ, cam.axisY);
                }
            }
        }

        var splatCount = gs.splatCount;
        if (splatCount != 0)
        {
            using var disabled = new EditorGUI.DisabledScope(true);
            EditorGUILayout.IntField("Splats", splatCount);
            EditorGUILayout.Vector3Field("Center", gs.bounds.center);
            EditorGUILayout.Vector3Field("Extent", gs.bounds.extents);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Calc Stats"))
            CalcStats(gs.pointCloudFolder);
        if (GUILayout.Button("Clear Stats", GUILayout.ExpandWidth(false)))
            ClearStats();
        GUILayout.EndHorizontal();

        if (m_StatsTexture && m_CachedDataRanges != null)
        {
            var distRect = GUILayoutUtility.GetRect(100, kFieldNames.Length * kRowHeight);
            var graphRect = new Rect(distRect.x + 70, distRect.y, distRect.width - 110, distRect.height);
            GUI.Box(graphRect, GUIContent.none);
            for (int bi = 0; bi < kFieldNames.Length; ++bi)
            {
                var rowRect = new Rect(distRect.x, distRect.y + bi * kRowHeight, distRect.width, kRowHeight);
                GUI.Label(new Rect(rowRect.x, rowRect.y, 30, rowRect.height), kFieldNames[bi], EditorStyles.miniLabel);
                GUI.Label(new Rect(rowRect.x + 30, rowRect.y, 40, rowRect.height),
                    m_CachedDataRanges[bi].x.ToString("F3"), EditorStyles.miniLabel);
                GUI.Label(new Rect(rowRect.xMax - 40, rowRect.y, 40, rowRect.height),
                    m_CachedDataRanges[bi].y.ToString("F3"), EditorStyles.miniLabel);
            }
            GUI.DrawTexture(graphRect, m_StatsTexture, ScaleMode.StretchToFill);
        }
    }

    struct Color64
    {
        public ushort r, g, b, a;
    }

    [BurstCompile]
    struct CalcStatsJob : IJobParallelFor
    {
        public int pixelsWidth;
        public int pixelsHeight;
        [NativeDisableParallelForRestriction] public NativeArray<Color64> pixels;
        public NativeArray<Vector2> ranges;
        [ReadOnly] public NativeArray<float> data;
        public int itemCount;
        public int itemStrideInFloats;

        static float AdjustVal(float val, int fieldIndex)
        {
            if (fieldIndex >= 55 && fieldIndex < 58) // scale: exp
            {
                //val = math.exp(val);
                //val = math.sqrt(val);
                //val = math.sqrt(val);
            }

            if (fieldIndex == 54) // opacity: sigmoid
            {
                //val = math.rcp(1.0f + math.exp(-val));
            }

            return val;
        }

        public void Execute(int fieldIndex)
        {
            // find min/max
            Vector2 range = new Vector2(float.PositiveInfinity, float.NegativeInfinity);
            int idx = fieldIndex;
            for (int si = 0; si < itemCount; ++si)
            {
                float val = AdjustVal(data[idx], fieldIndex);
                range.x = math.min(range.x, val);
                range.y = math.max(range.y, val);
                idx += itemStrideInFloats;
            }
            ranges[fieldIndex] = range;

            // fill texture with value distribution over the range
            idx = fieldIndex;
            for (int si = 0; si < itemCount; ++si)
            {
                float val = AdjustVal(data[idx], fieldIndex);
                val = math.unlerp(range.x, range.y, val);
                val = math.saturate(val);
                int px = (int) math.floor(val * pixelsWidth);
                int py = pixelsHeight - 2 - (fieldIndex * kRowHeight + 1 + (si % (kRowHeight - 4)));
                int pidx = py * pixelsWidth + px;

                Color64 col = pixels[pidx];
                col.r = (ushort)math.min(0xFFFF, col.r + 23);
                col.g = (ushort)math.min(0xFFFF, col.g + 7);
                col.b = (ushort)math.min(0xFFFF, col.b + 1);
                col.a = 0xFFFF;
                pixels[pidx] = col;

                idx += itemStrideInFloats;
            }
        }
    }

    void ClearStats()
    {
        m_CachedDataRanges = null;
        if (m_StatsTexture)
            DestroyImmediate(m_StatsTexture);
    }
    void CalcStats(string pointCloudFolder)
    {
        ClearStats();
        NativeArray<GaussianSplatRenderer.InputSplat> splats = GaussianSplatRenderer.LoadPLYSplatFile(pointCloudFolder);
        if (!splats.IsCreated)
            return;

        int itemSizeBytes = UnsafeUtility.SizeOf<GaussianSplatRenderer.InputSplat>();
        int fieldCount = itemSizeBytes / 4;

        if (!m_StatsTexture)
            m_StatsTexture = new Texture2D(512, fieldCount * kRowHeight, GraphicsFormat.R16G16B16A16_UNorm, TextureCreationFlags.None);
        NativeArray<Color64> statsPixels = new(m_StatsTexture.width * m_StatsTexture.height, Allocator.TempJob);
        NativeArray<Vector2> statsRanges = new(fieldCount, Allocator.TempJob);

        CalcStatsJob job;
        job.pixelsWidth = m_StatsTexture.width;
        job.pixelsHeight = m_StatsTexture.height;
        job.pixels = statsPixels;
        job.ranges = statsRanges;
        job.data = splats.Reinterpret<float>(itemSizeBytes);
        job.itemCount = splats.Length;
        job.itemStrideInFloats = fieldCount;
        job.Schedule(fieldCount, 1).Complete();

        m_StatsTexture.SetPixelData(statsPixels, 0);
        m_StatsTexture.Apply(false);
        m_CachedDataRanges = new Vector2[fieldCount];
        for (int i = 0; i < fieldCount; ++i)
            m_CachedDataRanges[i] = statsRanges[i];

        statsPixels.Dispose();
        statsRanges.Dispose();
        splats.Dispose();
    }
}