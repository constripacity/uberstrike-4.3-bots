"""
EXAMPLE 3: Quick Vision Test
Test the vision system works with your setup
"""
import cv2
import numpy as np
from pathlib import Path
import sys

# Add project root to path
sys.path.append(str(Path(__file__).parent.parent))

def test_vision_system():
    """Quick test to verify vision system works"""
    print("🧪 Quick Vision System Test")
    print("=" * 50)
    
    try:
        from vision_system.vision_integration import EnemyDetector
        
        # Initialize detector
        detector = EnemyDetector("vision_system/vision_model.pkl")
        
        # Create test frame
        test_frame = np.random.randint(0, 255, (480, 640, 3), dtype=np.uint8)
        
        # Add a "red enemy" for testing
        cv2.rectangle(test_frame, (200, 150), (250, 200), (0, 0, 255), -1)
        
        # Test detection
        print("Testing enemy detection...")
        enemies = detector.detect(test_frame)
        
        print(f"✅ Vision system working!")
        print(f"   Detected {len(enemies)} enemies")
        print(f"   Processing FPS: {detector.fps:.1f}")
        
        return True
        
    except Exception as e:
        print(f"❌ Test failed: {e}")
        import traceback
        traceback.print_exc()
        return False

if __name__ == "__main__":
    success = test_vision_system()
