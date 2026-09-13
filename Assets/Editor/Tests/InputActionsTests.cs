using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace VibeGame1.Tests
{
    public class InputActionsTests
    {
        [Test]
        public void TimingCaptureToggleUsesZeroKey()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            Assert.IsNotNull(asset);
            var action = asset.FindAction("Player/TimingCaptureToggle", true);
            Assert.That(action.bindings, Has.Exactly(1).Matches<InputBinding>(x => x.path == "<Keyboard>/0"));
        }
    }
}
