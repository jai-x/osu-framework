#!/bin/bash

dotnet tool restore
dotnet CodeFileSanity
dotnet jb inspectcode "osu-framework.Desktop.slnf" --output="inspectcodereport.xml" --caches-home="inspectcode" --verbosity=WARN
dotnet nvika parsereport "inspectcodereport.xml" --treatwarningsaserrors
