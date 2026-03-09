#!/usr/bin/env python3
"""
Simple PyPI publisher without twine
"""
import os
import sys
import requests
from pathlib import Path

PYPI_URL = "https://upload.pypi.org/legacy/"

def publish_to_pypi():
    """Upload distributions to PyPI"""
    dist_dir = Path("dist")
    
    if not dist_dir.exists():
        print("❌ dist/ directory not found. Run 'python -m build' first")
        return False
    
    # Get credentials from environment
    username = os.environ.get("PYPI_USERNAME")
    password = os.environ.get("PYPI_PASSWORD")
    
    if not username or not password:
        print("❌ PYPI_USERNAME and PYPI_PASSWORD environment variables not set")
        print("Set them: export PYPI_USERNAME=..., export PYPI_PASSWORD=...")
        return False
    
    # Find distribution files
    dists = list(dist_dir.glob("lokutor-1.1.1*"))
    
    if not dists:
        print("❌ No distribution files found for v1.1.1")
        return False
    
    print(f"📦 Found {len(dists)} distribution files to upload")
    
    for dist_file in dists:
        print(f"\n⬆️  Uploading {dist_file.name}...")
        
        with open(dist_file, "rb") as f:
            files = {"content": f}
            data = {
                "action": "file_upload",
                "protocol_version": "1",
            }
            
            response = requests.post(
                PYPI_URL,
                data=data,
                files=files,
                auth=(username, password),
                timeout=30
            )
            
            if response.status_code == 200:
                print(f"  ✅ Uploaded successfully")
            else:
                print(f"  ❌ Upload failed: {response.status_code}")
                print(f"  Response: {response.text[:200]}")
                return False
    
    print("\n✅ All distributions uploaded to PyPI!")
    return True

if __name__ == "__main__":
    success = publish_to_pypi()
    sys.exit(0 if success else 1)
