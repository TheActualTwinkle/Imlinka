#!/bin/bash

set -e

# Load version from .env
if [ -f .env ]; then
  source .env
elif [ -f scripts/.env ]; then
  source scripts/.env
else
  echo ".env file not found!"
  exit 1
fi

if [ -z "$VERSION" ]; then
  echo "VERSION not set in .env"
  exit 1
fi

echo Version = $VERSION

if [ -z "$NUGET_API_KEY" ]; then
  echo "NUGET_API_KEY environment variable not set"
  exit 1
fi

# Build all projects
dotnet clean
dotnet restore
dotnet build -c Release --no-restore

# Pack all projects
dotnet pack -c Release Imlinka/Imlinka.csproj --output nupkg

PACKAGE_PATH="nupkg/Imlinka.$VERSION.nupkg"
NUGET_SOURCE="https://api.nuget.org/v3/index.json"

echo "Will run:"
echo "dotnet nuget push \"$PACKAGE_PATH\" --api-key ***hidden*** --source \"$NUGET_SOURCE\""

read -r -p "Continue? [Y/n]: " CONFIRM
if [[ "$CONFIRM" =~ ^[Nn]$ ]]; then
  echo "Cancelled."
  exit 0
fi

dotnet nuget push "$PACKAGE_PATH" --api-key "$NUGET_API_KEY" --source "$NUGET_SOURCE"

echo "Done!"
