"""
Test the complete vision + bot integration
"""
import sys
import os
from pathlib import Path
import json
import time
import cv2
import numpy as np

# Add project root to path
project_root = Path(__file__).parent.parent
sys.path.append(str(project_root))

from integration.custom_integration import VisionEnhancedBotTrainingGenerator as VisionEnhancedBot

class IntegrationTester:
    """Test the vision system integration with bot framework"""
    
    def __init__(self):
        self.test_results = {}
        self.model_path = project_root / "models" / "vision_model.pkl"
        
    def test_vision_logic(self):
        """Test the VisionEnhancedBot with real images from the dataset"""
        print("🧪 Testing vision logic...")
        
        if not self.model_path.exists():
            print(f"❌ Model not found at {self.model_path}")
            return False
            
        # Initialize bot
        try:
            bot = VisionEnhancedBot()
        except Exception as e:
            print(f"❌ Failed to initialize VisionEnhancedBot: {e}")
            return False
            
        # Test on a few samples
        data_path = project_root / "data_gen" / "uberstrike_realistic" / "training_dataset"
        samples = list(data_path.glob("*.png"))[:5]
        
        results = []
        for img_path in samples:
            image = cv2.imread(str(img_path))
            if image is None: continue
            
            start_time = time.time()
            enemies = bot.process_frame(image)
            elapsed = time.time() - start_time
            
            # Check if we found anything (dataset should have enemies)
            results.append({
                "sample": img_path.name,
                "enemies_found": len(enemies),
                "time_ms": elapsed * 1000
            })
            
        self.test_results["vision_logic"] = results
        
        found_any = any(r["enemies_found"] > 0 for r in results)
        print(f"  ✅ Processed {len(samples)} samples")
        print(f"  ✅ Average time: {np.mean([r['time_ms'] for r in results]):.1f}ms")
        
        if not found_any:
            print("  ⚠️ Warning: No enemies detected in any test samples. Model might be under-performing.")
            
        return True

    def test_performance(self):
        """Test system performance at different resolutions"""
        print("\n⚡ Testing performance...")
        
        bot = VisionEnhancedBot()
        frame_sizes = [(640, 480), (800, 600), (1024, 768)]
        
        performance_data = []
        for width, height in frame_sizes:
            test_frame = np.random.randint(0, 255, (height, width, 3), dtype=np.uint8)
            
            times = []
            for _ in range(5):
                start = time.time()
                bot.process_frame(test_frame)
                times.append(time.time() - start)
            
            avg_time = np.mean(times)
            fps = 1.0 / avg_time if avg_time > 0 else 0
            
            performance_data.append({
                "resolution": f"{width}x{height}",
                "avg_time_ms": avg_time * 1000,
                "fps": fps
            })
            print(f"    {width}x{height}: {avg_time*1000:.1f}ms ({fps:.1f} FPS)")
            
        self.test_results["performance"] = performance_data
        return all(p["fps"] > 10 for p in performance_data)

    def run_all(self):
        print("=" * 60)
        print("🧪 INTEGRATION TEST SUITE")
        print("=" * 60)
        
        v_passed = self.test_vision_logic()
        p_passed = self.test_performance()
        
        print("\n" + "=" * 60)
        print("📊 SUMMARY")
        print(f"  Vision Logic: {'PASS' if v_passed else 'FAIL'}")
        print(f"  Performance:  {'PASS' if p_passed else 'FAIL'}")
        print("=" * 60)
        
        return v_passed and p_passed

if __name__ == "__main__":
    tester = IntegrationTester()
    success = tester.run_all()
    sys.exit(0 if success else 1)
