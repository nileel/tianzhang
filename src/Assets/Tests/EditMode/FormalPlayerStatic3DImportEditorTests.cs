using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class FormalPlayerStatic3DImportEditorTests
    {
        private const string ModelPath = "Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.fbx";
        private const string TexturePath = "Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D_BaseColor.png";
        private const float Tolerance = 0.0001f;

        // Approved v2 points use Blender's right-handed +Y-up/+Z-front asset basis.
        // Unity's left-handed basis negates X while preserving Y/up and Z/front.
        // Comparing asymmetric points also detects a reversed front that symmetric bounds cannot detect.
        private static readonly Vector3[] ApprovedPoints =
        {
            new Vector3(-0.0309452675f, 0.8312457204f, 0.1245358959f),
            new Vector3(-0.0128309866f, 0.8020615578f, -0.1104470119f),
            new Vector3(-0.1738470942f, 0.6994137764f, 0.0083024092f),
            new Vector3(0.1798851937f, 0.5519833565f, -0.0505691469f),
            new Vector3(-0.0948485509f, 0f, 0.0500659831f),
            new Vector3(-0.0138373282f, 1.0299999714f, -0.0420151651f)
        };

        [Test]
        public void ImportedGeometryRetainsApprovedSizeGroundingAndOrientation()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(importer, ModelPath);
            Assert.IsNotNull(model, ModelPath);
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(filters, Has.Length.EqualTo(1));
            Mesh mesh = filters[0].sharedMesh;
            Assert.IsNotNull(mesh);
            Assert.IsTrue(importer.isReadable);
            Vector3[] finalVertices = mesh.vertices.Select(filters[0].transform.TransformPoint).ToArray();
            Assert.That(finalVertices, Is.Not.Empty);
            var bounds = new Bounds(finalVertices[0], Vector3.zero);
            foreach (Vector3 vertex in finalVertices) bounds.Encapsulate(vertex);

            TestContext.WriteLine("IMPORT globalScale={0:R} fileScale={1:R} useFileScale={2} bakeAxisConversion={3}",
                importer.globalScale, importer.fileScale, importer.useFileScale, importer.bakeAxisConversion);
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
                TestContext.WriteLine("NODE {0} position={1} rotation={2} scale={3}", node.name,
                    Format(node.localPosition), node.localRotation.ToString("F8"), Format(node.localScale));
            TestContext.WriteLine("GEOMETRY vertices={0} triangles={1} localMin={2} localMax={3} finalMin={4} finalMax={5}",
                mesh.vertexCount, mesh.triangles.Length / 3, Format(mesh.bounds.min), Format(mesh.bounds.max),
                Format(bounds.min), Format(bounds.max));
            foreach (Vector3 signs in new[] { Vector3.one, new Vector3(-1, 1, 1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1) })
                TestContext.WriteLine("ANCHOR errors for signs {0}: {1}", Format(signs),
                    string.Join(",", ApprovedPoints.Select(point => NearestDistance(finalVertices, Vector3.Scale(point, signs))
                        .ToString("R", CultureInfo.InvariantCulture))));

            {
                Assert.IsTrue(importer.bakeAxisConversion);
                Assert.IsTrue(importer.useFileScale);
                Assert.AreEqual(1f, importer.globalScale, Tolerance);
                Assert.IsFalse(importer.importAnimation);
                Assert.IsFalse(importer.importBlendShapes);
                Assert.IsFalse(importer.importCameras);
                Assert.IsFalse(importer.importLights);
                foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(node.localPosition.magnitude, Is.LessThan(Tolerance), node.name + " position");
                    Assert.That(Quaternion.Angle(Quaternion.identity, node.localRotation), Is.LessThan(0.01f), node.name + " rotation");
                    // User approved positive uniform unit-conversion scale; final geometry is measured above.
                    Assert.That(node.localScale.x, Is.GreaterThan(0f), node.name + " scale");
                    Assert.AreEqual(node.localScale.x, node.localScale.y, Tolerance, node.name + " uniform scale");
                    Assert.AreEqual(node.localScale.x, node.localScale.z, Tolerance, node.name + " uniform scale");
                }
                Assert.That(mesh.triangles.Length / 3, Is.GreaterThan(0));
                Assert.IsTrue(finalVertices.All(v => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z)));
                Assert.IsTrue(finalVertices.Any(v => v.sqrMagnitude > 0));
                Assert.That(bounds.size.x, Is.GreaterThan(0f));
                Assert.That(bounds.size.y, Is.GreaterThan(0f));
                Assert.That(bounds.size.z, Is.GreaterThan(0f));
                Assert.AreEqual(0f, bounds.min.y, Tolerance, "ground");
                Assert.AreEqual(1.03f, bounds.max.y, Tolerance, "height");
                Assert.That(Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)), Is.LessThanOrEqualTo(0.30f + Tolerance));
                Assert.That(Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z)), Is.LessThanOrEqualTo(0.30f + Tolerance));
                foreach (Vector3 point in ApprovedPoints)
                {
                    Vector3 unityPoint = new Vector3(-point.x, point.y, point.z);
                    Assert.That(NearestDistance(finalVertices, unityPoint), Is.LessThan(Tolerance), "approved geometry point " + Format(unityPoint));
                }
                Assert.That(model.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
                Assert.That(model.GetComponentsInChildren<Animator>(true), Is.Empty);
                Assert.That(model.GetComponentsInChildren<Animation>(true), Is.Empty);
                Assert.That(mesh.blendShapeCount, Is.Zero);
                Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void BaseColorRetainsApprovedDimensionsAndOpaqueImport()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Assert.IsNotNull(importer);
            Assert.IsNotNull(texture);
            {
                Assert.AreEqual(TextureImporterType.Default, importer.textureType);
                Assert.IsTrue(importer.sRGBTexture);
                Assert.AreEqual(TextureImporterAlphaSource.None, importer.alphaSource);
                Assert.IsFalse(importer.alphaIsTransparency);
                Assert.AreEqual(4096, texture.width);
                Assert.AreEqual(4096, texture.height);
            }
        }

        private static float NearestDistance(Vector3[] vertices, Vector3 point)
        {
            float distance = float.PositiveInfinity;
            foreach (Vector3 vertex in vertices) distance = Mathf.Min(distance, (vertex - point).sqrMagnitude);
            return Mathf.Sqrt(distance);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string Format(Vector3 value) => value.ToString("F8");
    }
}
