using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class TouchGestureTests
    {
        [Test]
        public void Gesture_ShouldTriggerOnceAfterTheHoldTime()
        {
            var gesture = new MasterMemoryTouchGesture();
            Assert.IsFalse(gesture.Update(3, 3, 1f, 10f));
            Assert.IsFalse(gesture.Update(3, 3, 1f, 10.5f));
            Assert.IsTrue(gesture.Update(4, 3, 1f, 11f), "more fingers also count");
            Assert.IsFalse(gesture.Update(3, 3, 1f, 12f), "only once while held");

            Assert.IsFalse(gesture.Update(0, 3, 1f, 12.1f));
            Assert.IsFalse(gesture.Update(3, 3, 1f, 13f));
            Assert.IsTrue(gesture.Update(3, 3, 1f, 14f), "again after lifting the fingers");
        }

        [Test]
        public void Gesture_ShouldRestartWhenAFingerIsLifted()
        {
            var gesture = new MasterMemoryTouchGesture();
            Assert.IsFalse(gesture.Update(3, 3, 1f, 0f));
            Assert.IsFalse(gesture.Update(2, 3, 1f, 0.9f));
            Assert.IsFalse(gesture.Update(3, 3, 1f, 1.2f));
            Assert.IsTrue(gesture.Update(3, 3, 1f, 2.2f));
        }

        [Test]
        public void Gesture_ShouldBeDisabledWithZeroFingers()
        {
            var gesture = new MasterMemoryTouchGesture();
            Assert.IsFalse(gesture.Update(0, 0, 0.1f, 0f));
            Assert.IsFalse(gesture.Update(5, 0, 0.1f, 10f));
        }
    }
}
