#!/usr/bin/env python3
"""
Direct PyPI Upload using only stdlib (no requests required)
"""
import os
import sys
import urllib.request
import urllib.error
import base64
import hashlib
import json
from pathlib import Path

def upload_to_pypi(username, password):
    """Upload distributions to PyPI using urllib"""
    PYPI_URL = "https://upload.pypi.org/legacy/"
    dist_dir = Path("dist")
    
    if not dist_dir.exists():
        print("❌ dist/ directory not found. Run 'python -m build' first")
        return False
    
    dists = list(dist_dir.glob("lokutor-1.1.1*"))
    
    if not dists:
        print("❌ No distribution files found for v1.1.1")
        return False
    
    print(f"📦 Found {len(dists)} distribution files to upload")
    
    # Create authorization header
    credentials = f"{username}:{password}".encode('utf-8')
    auth_header = "Basic " + base64.b64encode(credentials).decode('ascii')
    
    for dist_file in dists:
        print(f"\n⬆️  Uploading {dist_file.name}...")
        
        with open(dist_file, "rb") as f:
            file_data = f.read()
        
        # Calculate MD5 hash for integrity check
        md5_hash = hashlib.md5(file_data).hexdigest()
        
        # Prepare multipart form data
        boundary = "----" + hashlib.md5(str(dist_file).encode()).hexdigest()
        body = []
        
        # Add form fields
        fields = {
            "action": "file_upload",
            "protocol_version": "1",
        }
        
        for key, value in fields.items():
            body.append(f'--{boundary}'.encode())
            body.append(f'Content-Disposition: form-data; name="{key}"'.encode())
            body.append(b'')
            body.append(value.encode() if isinstance(value, str) else value)
        
        # Add file field
        body.append(f'--{boundary}'.encode())
        body.append(f'Content-Disposition: form-data; name="content"; filename="{dist_file.name}"'.encode())
        body.append(b'Content-Type: application/octet-stream')
        body.append(b'')
        body.append(file_data)
        body.append(f'--{boundary}--'.encode())
        body.append(b'')
        
        body_bytes = b'\n'.join(body)
        
        # Create request
        req = urllib.request.Request(
            PYPI_URL,
            data=body_bytes,
            method="POST"
        )
        
        req.add_header("Authorization", auth_header)
        req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
        
        try:
            with urllib.request.urlopen(req, timeout=30) as response:
                status = response.status
                if status == 200:
                    print(f"  ✅ Uploaded successfully (HTTP {status})")
                else:
                    print(f"  ⚠️  Response code: {status}")
        except urllib.error.HTTPError as  e:
            print(f"  ❌ Upload failed: HTTP {e.code}")
            if e.code == 403:
                print("     (403 Forbidden - check credentials or package name)")
            elif e.code == 404:
                print("     (404 Not Found - package may not be registered)")
            return False
        except urllib.error.URLError as e:
            print(f"  ❌ Connection error: {e.reason}")
            return False
    
    print("\n✅ All distributions uploaded to PyPI!")
    return True

if __name__ == "__main__":
    username = os.environ.get("PYPI_USERNAME") or input("PyPI Username: ")
    password = os.environ.get("PYPI_PASSWORD") or input("PyPI Password/Token: ")
    
    if not username or not password:
        print("❌ Credentials required")
        sys.exit(1)
    
    success = upload_to_pypi(username, password)
    sys.exit(0 if success else 1)
