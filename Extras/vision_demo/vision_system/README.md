# 🤖 UberStrike Vision System

Deployed: 2026-01-05 23:32
Accuracy: 85.7%
Target FPS: ~10

## Disclaimer
**EDUCATIONAL AND RESEARCH USE ONLY.** This project is developed for academic purposes to study computer vision in gaming environments. It is intended for offline practice and AI research. Using this in online multiplayer may violate the Terms of Service of the game. Use responsibly and fairly.

## Features
- **RandomForest Classifier**: 85.7% pixel-level accuracy.
- **Optimized Pipeline**: 10 FPS real-time processing.
- **Easy Integration**: Drop-in wrapper for existing bot frameworks.
- **Distance Estimation**: Size-based distance heuristics.

## Requirements
- OpenCV (`opencv-python`)
- NumPy
- scikit-learn

## Installation
1. Copy the `vision_system` folder to your project.
2. Install dependencies:
   ```bash
   pip install -r Extras/vision_demo/requirements.txt
   ```
3. Run the standalone smoke test (from the repository root):
   ```bash
   python Extras/vision_demo/vision_system/test_vision.py
   ```
3. Run the standalone smoke test (from the repository root):
   ```bash
   python vision_system/test_vision.py
   ```

## Usage
Import the `VisionEnhancedBot` and use it in your game loop (standalone Python helper; not wired to the C# BotRunner):

```python
from vision_system.vision_integration import VisionEnhancedBot

bot = VisionEnhancedBot()
# In your loop:
result = bot.update_with_vision(frame)
print(result['enemies'])
```

## Examples
See `test_vision.py` for a complete example of the detection pipeline.
