# Runs the actual calculation methods with lightweight geometry stubs, without Revit.
#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$wallSource = Get-Content (Join-Path $projectRoot 'WallData.cs') -Raw
function Get-MethodText([string]$source, [string]$name) {
    $match = [regex]::Match($source, 'private static [^\r\n]+ ' + $name + '\(')
    if (-not $match.Success) { throw "Method not found: $name" }
    $brace = $source.IndexOf('{', $match.Index)
    $depth = 1
    $end = $brace + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($match.Index, $end - $match.Index).Replace('private static', 'public static')
}
$methods = @('BuildVerticalWallBranches', 'FindOpenings', 'FindConcreteClass') |
    ForEach-Object { Get-MethodText $wallSource $_ }
$prelude = @'
using System;
using System.Collections.Generic;
using System.Linq;
public class XYZ {
    public double X, Y, Z;
    public XYZ(double x,double y,double z) { X=x;Y=y;Z=z; }
    public static XYZ operator +(XYZ a,XYZ b) { return new XYZ(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
    public static XYZ operator *(XYZ a,double b) { return new XYZ(a.X*b,a.Y*b,a.Z*b); }
    public double DotProduct(XYZ b) { return X*b.X+Y*b.Y+Z*b.Z; }
    public double GetLength() { return Math.Sqrt(X*X+Y*Y+Z*Z); }
    public double DistanceTo(XYZ b) { return new XYZ(X-b.X,Y-b.Y,Z-b.Z).GetLength(); }
}
public class ElementId { public int IntegerValue=1; }
public class Wall { public string Name; public ElementId Id=new ElementId(); }
public class Document { }
public class Solid { }
public class WallData {
    public Wall Wall; public XYZ StartPoint,EndPoint,Direction;
    public double BottomZ,TopZ,Length;
}
public class GapData { public double Left,Right,MinDetectedZ,MaxDetectedZ; }
public class OpeningData { public double Left,Right,BottomZ,TopZ,LeftMm,RightMm; }
public class ConcreteInfo { public string Name; public double LapFactor; }
public static class RegressionHarness {
    const double ZToleranceMm=5, OpeningScanStepMm=50;
    static double ToFeet(double mm) { return mm/304.8; }
    static double ToMm(double feet) { return feet*304.8; }
    static string TryGetConcreteClass(Document doc,Wall wall) { return wall.Name; }
    public static bool SeparatedOpenings;
    static List<GapData> FindGapsAtLevel(WallData wall,List<Solid> solids,double z) {
        double mm=ToMm(z);
        bool gap=(mm>=99 && mm<=201) || (mm>=349 && mm<=451);
        if (!SeparatedOpenings) gap=mm>=99 && mm<=451;
        return gap ? new List<GapData> { new GapData {Left=ToFeet(100),Right=ToFeet(200)} }
                   : new List<GapData>();
    }
    static double FindVerticalTransition(WallData wall,List<Solid> solids,double x,double z,int direction) {
        return z+direction*ToFeet(25);
    }
    static WallData W(double bottom,double top,bool reverse=false,double shift=0) {
        return new WallData { Wall=new Wall {Name="C25/30"},
            StartPoint=new XYZ(ToFeet(reverse?3000:0),ToFeet(shift),0),
            EndPoint=new XYZ(ToFeet(reverse?0:3000),ToFeet(shift),0),
            Direction=new XYZ(reverse?-1:1,0,0), Length=ToFeet(3000),
            BottomZ=ToFeet(bottom),TopZ=ToFeet(top) };
    }
    static void Check(bool ok,string name) { if (!ok) throw new Exception(name); }
    public static int Run() {
        Check(BuildVerticalWallBranches(new List<WallData>{W(0,3000),W(3000,6000)}).Count==1,"adjacent walls");
        Check(BuildVerticalWallBranches(new List<WallData>{W(0,3000),W(4000,7000)}).Count==2,"vertical gap");
        Check(BuildVerticalWallBranches(new List<WallData>{W(0,3000),W(3000,6000,true)}).Count==1,"reversed axis");
        Check(BuildVerticalWallBranches(new List<WallData>{W(0,3000),W(3000,6000,false,100)}).Count==2,"shifted wall");
        Check(BuildVerticalWallBranches(new List<WallData>{W(0,3000),W(2000,5000)}).Count==2,"overlapping heights");
        Check(BuildVerticalWallBranches(new List<WallData>{W(0,3000),W(3000,6000),W(7000,10000)}).Count==2,"gap after continuation");
        var rotated=W(3000,6000); rotated.StartPoint.Y=ToFeet(-100); rotated.EndPoint.Y=ToFeet(100);
        Check(BuildVerticalWallBranches(new List<WallData>{W(0,3000),rotated}).Count==2,"different endpoints");
        SeparatedOpenings=true;
        Check(FindOpenings(W(0,600),new List<Solid>()).Count==2,"stacked openings");
        SeparatedOpenings=false;
        Check(FindOpenings(W(0,600),new List<Solid>()).Count==1,"continuous opening");
        Check(FindConcreteClass(new Document(),new List<WallData>{W(0,3000),W(3000,6000)}).LapFactor==55,"uniform concrete");
        var mixed=W(3000,6000); mixed.Wall.Name="C20/25";
        bool rejected=false;
        try {FindConcreteClass(new Document(),new List<WallData>{W(0,3000),mixed});} catch(InvalidOperationException){rejected=true;}
        Check(rejected,"mixed concrete rejected");
        mixed.Wall.Name="C20/25 C25/30"; rejected=false;
        try {FindConcreteClass(new Document(),new List<WallData>{mixed});} catch(InvalidOperationException){rejected=true;}
        Check(rejected,"ambiguous concrete rejected");
        mixed.Wall.Name="unknown"; rejected=false;
        try {FindConcreteClass(new Document(),new List<WallData>{mixed});} catch(InvalidOperationException){rejected=true;}
        Check(rejected,"unknown concrete rejected");
        return 13;
    }
'@
Add-Type -TypeDefinition ($prelude + ($methods -join "`n") + "`n}")
$count = [RegressionHarness]::Run()
# Evaluate the expressions from all three node implementations at 12, 14 and 16 mm.
foreach ($file in @('CornerVerticalRebarService.cs', 'TJointVerticalRebarService.cs')) {
    $source = Get-Content (Join-Path $projectRoot $file) -Raw
    $expressions = [regex]::Matches($source, 'double lap12Mm\s*=\s*([^;]+);')
    foreach ($expression in $expressions) {
        foreach ($diameter in @(12,14,16)) {
            $formula = $expression.Groups[1].Value.Replace('lapMmFor10','550').Replace('ToMm(GetBarDiameterFeet(barType))',"$diameter")
            $value = Invoke-Expression $formula
            if ([Math]::Abs($value - 55*$diameter) -gt 0.001) { throw "Incorrect lap in $file for diameter $diameter" }
            $count++
        }
    }
}
Write-Output "Passed $count regression cases (calculation methods; Revit geometry is stubbed)."
