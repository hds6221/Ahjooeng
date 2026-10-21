using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
[assembly: CommandClass(typeof(LegendSourceExport))]
[DataContract] public class SourceEntity
{
    [DataMember] public string Handle;
    [DataMember] public string Type;
    [DataMember] public string Layer;
    [DataMember] public string Text;
    [DataMember] public double X;
    [DataMember] public double Y;
    [DataMember] public double Height;
    [DataMember] public double MinX;
    [DataMember] public double MinY;
    [DataMember] public double MaxX;
    [DataMember] public double MaxY;
}
public class LegendSourceExport
{
    [CommandMethod("LGENEXPORT")]
    public void Run()
    {
        string sourcePath = Environment.GetEnvironmentVariable("LEGEND_MASTER_DWG");
        string output = Environment.GetEnvironmentVariable("LEGEND_EXPORT_OUTPUT");
        Directory.CreateDirectory(output);
        try
        {
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(sourcePath, FileOpenMode.OpenForReadAndAllShare, true, null); db.CloseInput(true);
                var rows = new List<SourceEntity>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in model)
                    {
                        var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (entity == null) continue;
                        var row = new SourceEntity { Handle = entity.Handle.ToString(), Type = entity.GetRXClass().DxfName, Layer = entity.Layer, Text = "" };
                        try { var extents = entity.GeometricExtents; row.MinX = extents.MinPoint.X; row.MinY = extents.MinPoint.Y; row.MaxX = extents.MaxPoint.X; row.MaxY = extents.MaxPoint.Y; } catch { }
                        var text = entity as DBText;
                        if (text != null) { row.Text = text.TextString; row.X = text.AlignmentPoint.X; row.Y = text.AlignmentPoint.Y; row.Height = text.Height; if (text.HorizontalMode == TextHorizontalMode.TextLeft && text.VerticalMode == TextVerticalMode.TextBase) { row.X = text.Position.X; row.Y = text.Position.Y; } }
                        var mtext = entity as MText;
                        if (mtext != null) { row.Text = mtext.Text; row.X = mtext.Location.X; row.Y = mtext.Location.Y; row.Height = mtext.TextHeight; }
                        var block = entity as BlockReference;
                        if (block != null) { row.Text = ((BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead)).Name; row.X = block.Position.X; row.Y = block.Position.Y; }
                        rows.Add(row);
                    }
                }
                using (var stream = File.Create(Path.Combine(output, "source-entities.json"))) new DataContractJsonSerializer(typeof(List<SourceEntity>)).WriteObject(stream, rows);
                db.DxfOut(Path.Combine(output, "범례템플릿.dxf"), 16, DwgVersion.AC1032);
                File.WriteAllText(Path.Combine(output, "export-success.txt"), "PASS: master DWG exported read-only; entities=" + rows.Count);
            }
        }
        catch (System.Exception ex) { File.WriteAllText(Path.Combine(output, "export-error.txt"), ex.ToString()); }
    }
}
