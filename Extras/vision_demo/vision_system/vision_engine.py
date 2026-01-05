import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
import cv2
import numpy as np
import json
from pathlib import Path
import time

class UNet(nn.Module):
    """
    Lightweight U-Net for pixel-wise classification.
    Ideal for detecting enemies against complex backgrounds.
    """
    def __init__(self, in_channels=3, out_channels=1):
        super(UNet, self).__init__()
        
        def conv_block(in_c, out_c):
            return nn.Sequential(
                nn.Conv2d(in_c, out_c, kernel_size=3, padding=1),
                nn.BatchNorm2d(out_c),
                nn.ReLU(inplace=True),
                nn.Conv2d(out_c, out_c, kernel_size=3, padding=1),
                nn.BatchNorm2d(out_c),
                nn.ReLU(inplace=True)
            )

        self.enc1 = conv_block(in_channels, 32)
        self.enc2 = conv_block(32, 64)
        self.enc3 = conv_block(64, 128)
        
        self.pool = nn.MaxPool2d(2)
        
        self.bottleneck = conv_block(128, 256)
        
        self.up3 = nn.ConvTranspose2d(256, 128, kernel_size=2, stride=2)
        self.dec3 = conv_block(256, 128)
        self.up2 = nn.ConvTranspose2d(128, 64, kernel_size=2, stride=2)
        self.dec2 = conv_block(128, 64)
        self.up1 = nn.ConvTranspose2d(64, 32, kernel_size=2, stride=2)
        self.dec1 = conv_block(64, 32)
        
        self.final = nn.Conv2d(32, out_channels, kernel_size=1)
        self.sigmoid = nn.Sigmoid()

    def forward(self, x):
        e1 = self.enc1(x)
        e2 = self.enc2(self.pool(e1))
        e3 = self.enc3(self.pool(e2))
        
        b = self.bottleneck(self.pool(e3))
        
        d3 = self.up3(b)
        d3 = torch.cat([d3, e3], dim=1)
        d3 = self.dec3(d3)
        
        d2 = self.up2(d3)
        d2 = torch.cat([d2, e2], dim=1)
        d2 = self.dec2(d2)
        
        d1 = self.up1(d2)
        d1 = torch.cat([d1, e1], dim=1)
        d1 = self.dec1(d1)
        
        return self.sigmoid(self.final(d1))

class appDataset(Dataset):
    def __init__(self, dataset_path: Path, target_size=(256, 256)):
        self.path = dataset_path
        self.target_size = target_size
        self.samples = list(self.path.glob("sample_*.png"))
        self.samples = [s for s in self.samples if "_mask" not in s.name]
        
    def __len__(self):
        return len(self.samples)
        
    def __getitem__(self, idx):
        img_path = self.samples[idx]
        mask_path = img_path.parent / f"{img_path.stem}_mask.png"
        
        # Read image and mask
        image = cv2.imread(str(img_path))
        mask = cv2.imread(str(mask_path), cv2.IMREAD_GRAYSCALE)
        
        # Resize for fixed-size network input
        image = cv2.resize(image, self.target_size)
        mask = cv2.resize(mask, self.target_size)
        
        # Normalize and convert to tensors
        image = image.transpose(2, 0, 1).astype(np.float32) / 255.0
        mask = (mask > 0).astype(np.float32)[np.newaxis, :, :]
        
        return torch.from_numpy(image), torch.from_numpy(mask)

def train_model():
    print("🚀 INITIALIZING DEEP TRAINING ON GPU")
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    print(f"Using device: {device} ({torch.cuda.get_device_name(0)})")
    
    # Dataset and Loader
    dataset_path = Path("data_gen/uberstrike_realistic/training_dataset")
    dataset = appDataset(dataset_path)
    
    # Split train/val
    train_size = int(0.8 * len(dataset))
    val_size = len(dataset) - train_size
    train_ds, val_ds = torch.utils.data.random_split(dataset, [train_size, val_size])
    
    train_loader = DataLoader(train_ds, batch_size=32, shuffle=True, num_workers=4)
    val_loader = DataLoader(val_ds, batch_size=32, shuffle=False)
    
    # Model, Loss, Optimizer
    model = UNet().to(device)
    criterion = nn.BCELoss()
    optimizer = optim.Adam(model.parameters(), lr=0.001)
    
    # Training Loop
    epochs = 20
    best_loss = float('inf')
    
    for epoch in range(epochs):
        model.train()
        train_loss = 0
        start_time = time.time()
        
        for images, masks in train_loader:
            images, masks = images.to(device), masks.to(device)
            
            optimizer.zero_grad()
            outputs = model(images)
            loss = criterion(outputs, masks)
            loss.backward()
            optimizer.step()
            
            train_loss += loss.item()

        # Print GPU stats after each epoch
        if device.type == 'cuda':
            allocated = torch.cuda.memory_allocated(0) / 1024**2
            reserved = torch.cuda.memory_reserved(0) / 1024**2
            print(f"GPU Memory: {allocated:.0f}MB allocated, {reserved:.0f}MB reserved")
            
        # Validation
        model.eval()
        val_loss = 0
        with torch.no_grad():
            for images, masks in val_loader:
                images, masks = images.to(device), masks.to(device)
                outputs = model(images)
                loss = criterion(outputs, masks)
                val_loss += loss.item()
        
        avg_train_loss = train_loss / len(train_loader)
        avg_val_loss = val_loss / len(val_loader)
        duration = time.time() - start_time
        
        print(f"Epoch {epoch+1}/{epochs} | Train Loss: {avg_train_loss:.4f} | Val Loss: {avg_val_loss:.4f} | {duration:.1f}s")
        
        if avg_val_loss < best_loss:
            best_loss = avg_val_loss
            torch.save(model.state_dict(), "models/cache_v1.bin")
            print("⭐ Model saved!")

    print("\n✅ Training Complete!")

if __name__ == "__main__":
    Path("models").mkdir(exist_ok=True)
    train_model()
