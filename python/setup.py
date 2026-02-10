from setuptools import setup, find_packages

with open("README.md", "r", encoding="utf-8") as fh:
    long_description = fh.read()

setup(
    name="lokutor-voice-agent",
    version="1.0.0",
    description="Production-ready Python SDK for Lokutor Voice Agent - Real-time AI voice conversations",
    long_description=long_description,
    long_description_content_type="text/markdown",
    author="Lokutor",
    author_email="support@lokutor.com",
    url="https://github.com/lokutor/sdk-python",
    python_requires=">=3.8",
    packages=find_packages(),
    install_requires=[
        "websocket-client>=1.0.0,<2.0.0",
        "pyaudio>=0.2.13",
        "python-dotenv>=1.0.0",
    ],
    extras_require={
        "dev": [
            "pytest>=6.0",
            "pytest-asyncio>=0.18.0",
            "black>=22.0",
            "flake8>=4.0",
            "mypy>=0.900",
        ],
        "docs": [
            "sphinx>=4.0",
            "sphinx-rtd-theme>=1.0",
        ],
    },
    classifiers=[
        "Development Status :: 5 - Production/Stable",
        "Intended Audience :: Developers",
        "License :: OSI Approved :: MIT License",
        "Programming Language :: Python :: 3",
        "Programming Language :: Python :: 3.8",
        "Programming Language :: Python :: 3.9",
        "Programming Language :: Python :: 3.10",
        "Programming Language :: Python :: 3.11",
        "Programming Language :: Python :: 3.12",
        "Topic :: Software Development :: Libraries :: Python Modules",
        "Topic :: Multimedia :: Sound/Audio",
        "Topic :: Communications",
    ],
    keywords="voice ai agent conversation tts stt llm",
    project_urls={
        "Bug Reports": "https://github.com/lokutor/sdk-python/issues",
        "Documentation": "https://lokutor.com/docs",
        "Source": "https://github.com/lokutor/sdk-python",
    },
)
