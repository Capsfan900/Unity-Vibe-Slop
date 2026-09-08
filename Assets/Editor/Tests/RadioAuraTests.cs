using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Pins the shipped rainbow border around the radio pane: that it exists, that it is a real UI
    /// shader URP will draw, and that its decoration can never cross the HUD's bloom budget.
    /// A shader default proves nothing — every assertion here reads the .mat.
    /// </summary>
    public class RadioAuraTests
    {
        const string MaterialPath = "Assets/Materials/M_RadioAura.mat";
        const string ShaderName = "VibeGame1/UI/RainbowBorder";

        static Material Load()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.IsNotNull(material, "run VibeGame1/2. Create Materials before testing the radio aura");
            return material;
        }

        [Test]
        public void ShaderIsASupportedUrpUiShaderWithNoCompileMessages()
        {
            var material = Load();
            Assert.IsNotNull(material.shader);
            Assert.AreEqual(ShaderName, material.shader.name);
            Assert.IsTrue(material.shader.isSupported, "the radio border shader must compile on this target");

            var messages = ShaderUtil.GetShaderMessages(material.shader);
            if (messages != null && messages.Length > 0)
            {
                var text = "";
                foreach (var m in messages) text += m.message + " (" + m.file + ":" + m.line + ")\n";
                Assert.Fail("RainbowBorder.shader reported compile messages:\n" + text);
            }

            Assert.AreEqual("UniversalPipeline", material.GetTag("RenderPipeline", false),
                "a custom shader without the UniversalPipeline SubShader tag fails Health Check");
            Assert.AreEqual("Transparent", material.GetTag("RenderType", false));
            Assert.AreEqual((int)RenderQueue.Transparent, material.renderQueue);
        }

        [Test]
        public void BorderTracesTheShippedPaneAndLeavesItsInteriorClear()
        {
            var material = Load();

            // The pane is 300 x 116 with roughly a 12 px UiSprites.Pane() corner. Geometry is expressed
            // in fractions of the rect HEIGHT, and RainbowBorderView overwrites _Aspect from the live
            // RectTransform, so this is the fallback, not a hardcoded layout.
            Assert.AreEqual(300f / 116f, material.GetFloat("_Aspect"), 0.01f);
            Assert.AreEqual(0.103f, material.GetFloat("_Radius"), 0.001f);
            Assert.AreEqual(0.055f, material.GetFloat("_Thickness"), 0.001f);
            Assert.AreEqual(0.012f, material.GetFloat("_Feather"), 0.001f);

            float thickness = material.GetFloat("_Thickness") * 116f;
            Assert.That(thickness, Is.InRange(3f, 10f), "the border must read as a frame, not as a slab");
            Assert.Less(thickness + material.GetFloat("_Feather") * 116f, 16f,
                "the lit band must stay outside the pane's 16 px inner padding so the ticker stays readable");
            Assert.Less(material.GetFloat("_Thickness") * 2f, 1f,
                "the two bands must never meet across the short axis, or the interior stops being clear");
        }

        [Test]
        public void SwirlTravelsSlowlyEnoughToStayPeripheral()
        {
            var material = Load();
            Assert.AreEqual(1f, material.GetFloat("_HueCycles"), 0.001f,
                "one spectrum per lap: a whole rainbow at rest, not a strobing repeat");
            Assert.AreEqual(0.18f, material.GetFloat("_SwirlSpeed"), 0.001f);
            Assert.AreEqual(0.42f, material.GetFloat("_CometSpeed"), 0.001f);
            Assert.AreEqual(0.34f, material.GetFloat("_CometLength"), 0.001f);
            Assert.That(material.GetFloat("_SwirlSpeed"), Is.InRange(0.05f, 0.5f),
                "a lap slower than 2 s would compete with a combat tell for the eye");
            Assert.AreNotEqual(material.GetFloat("_SwirlSpeed"), material.GetFloat("_CometSpeed"),
                "hue and comet must drift apart or the frame locks into one rigid pattern");
        }

        [Test]
        public void DecorationStaysUnderTheHudBloomBudget()
        {
            var material = Load();
            float peak = material.GetFloat("_Peak");
            float gain = material.GetFloat("_CometGain");
            float alpha = material.GetFloat("_Alpha");
            Assert.AreEqual(0.79f, peak, 0.001f);
            Assert.AreEqual(0.26f, gain, 0.001f);
            Assert.AreEqual(0.85f, alpha, 0.001f);
            Assert.AreEqual(0.78f, material.GetFloat("_Saturation"), 0.001f);

            // The shader's brightest fragment is _Peak * (1 - g + 2g) at the comet head, before its own
            // clamp. 1.0, not the 1.05 navigational cap: this is a graphic on the HUD CANVAS, and the
            // UI never blooms (HudGlassTests holds the same line for every Image on it). Light on this
            // screen means "you deflected"; the music player never borrows that meaning.
            Assert.LessOrEqual(peak * (1f + gain), 1f,
                "the comet head must stay under 1.0 before the shader clamps - the UI never blooms");
            Assert.LessOrEqual(material.GetColor("_Color").maxColorComponent, 1f,
                "the tint must not smuggle HDR past the peak");
            Assert.Less(alpha, 1f, "the frame is glass-adjacent decoration, not an opaque bezel");
        }

        [Test]
        public void ViewFallsBackToThePaneRatioRatherThanDividingByZero()
        {
            Assert.AreEqual(300f / 116f, RainbowBorderView.FallbackAspect, 0.0001f);
            Assert.AreEqual(RainbowBorderView.FallbackAspect, RainbowBorderView.AspectOf(null), 0.0001f);

            var go = new GameObject("~RadioAuraRect", typeof(RectTransform));
            try
            {
                var rect = go.GetComponent<RectTransform>();
                rect.sizeDelta = Vector2.zero;
                Assert.AreEqual(RainbowBorderView.FallbackAspect, RainbowBorderView.AspectOf(rect), 0.0001f,
                    "a layout that has not run yet must not hand the shader a NaN aspect");
                rect.sizeDelta = new Vector2(300f, 116f);
                Assert.AreEqual(300f / 116f, RainbowBorderView.AspectOf(rect), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
