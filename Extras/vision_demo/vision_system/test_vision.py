"""
EXAMPLE: Quick Vision Test
Runs a lightweight smoke test against the packaged model.
"""
import sys
from pathlib import Path
import traceback

import cv2
import numpy as np

# Add project root (parent of vision_demo) so `vision_system` can be imported
sys.path.append(str(Path(__file__).resolve().parent.parent))


def test_vision_system() -> bool:
    """Quick test to verify vision system works"""
    print("🧪 Quick Vision System Test")
    print("=" * 50)

    try:
        from vision_system.vision_integration import EnemyDetector

        detector = EnemyDetector()

        # Create test frame with a "red enemy"
        test_frame = np.zeros((480, 640, 3), dtype=np.uint8)
        cv2.rectangle(test_frame, (200, 150), (260, 210), (0, 0, 255), -1)

        print("Testing enemy detection...")
        enemies = detector.detect(test_frame)

        print("✅ Vision system completed")
        print(f"   Detected {len(enemies)} enemies")
        print(f"   Processing FPS: {detector.fps:.1f}")
        return True

    except Exception as exc:
        print(f"❌ Test failed: {exc}")
        traceback.print_exc()
        return False


if __name__ == "__main__":
    ok = test_vision_system()
    sys.exit(0 if ok else 1)
