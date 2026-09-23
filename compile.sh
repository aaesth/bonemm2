#!/bin/bash

CSPROJ=$(ls *.csproj 2>/dev/null | head -n 1)

if [ -z "$CSPROJ" ]; then
    echo "Error: No .csproj file found in the current directory."
    exit 1
fi

CURRENT_VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$CSPROJ")
HAS_VERSION=true

if [ -z "$CURRENT_VERSION" ]; then
    CURRENT_VERSION="None (Defaults to 1.0.0)"
    HAS_VERSION=false
fi

echo "Current Project Version: $CURRENT_VERSION"
read -p "Do you want to change the version number? [y/N]: " UPDATE_CHOICE

if [[ "$UPDATE_CHOICE" =~ ^[Yy]$ ]]; then
    read -p "Enter new version (e.g., 1.0.1): " NEW_VERSION
    
    if [ "$HAS_VERSION" = true ]; then
        sed -i -E "s/<Version>.*<\/Version>/<Version>$NEW_VERSION<\/Version>/" "$CSPROJ"
    else
        sed -i "/<PropertyGroup>/a \    <Version>$NEW_VERSION</Version>" "$CSPROJ"
    fi
    echo "Updated $CSPROJ to Version $NEW_VERSION"
fi

echo -e "\nCompiling Linux binary..."
dotnet publish -c Release -r linux-x64 --self-contained false

echo -e "\nCompiling Windows binary..."
dotnet publish -c Release -r win-x64 --self-contained false

echo -e "\nDone!"