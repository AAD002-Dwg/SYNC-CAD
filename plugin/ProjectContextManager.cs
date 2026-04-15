using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System;

namespace CadSyncPlugin
{
    public static class ProjectContextManager
    {
        private const string MetaDictionaryName = "SYNC_CAD_META";
        private const string ProjectIdRecordName = "PROJECT_ID";

        public static void BindProject(Document doc, string projectId)
        {
            if (doc == null || string.IsNullOrEmpty(projectId)) return;

            Database db = doc.Database;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                DBDictionary metaDict;
                if (nod.Contains(MetaDictionaryName))
                {
                    metaDict = (DBDictionary)tr.GetObject(nod.GetAt(MetaDictionaryName), OpenMode.ForWrite);
                }
                else
                {
                    metaDict = new DBDictionary();
                    nod.SetAt(MetaDictionaryName, metaDict);
                    tr.AddNewlyCreatedDBObject(metaDict, true);
                }

                Xrecord xRec;
                if (metaDict.Contains(ProjectIdRecordName))
                {
                    xRec = (Xrecord)tr.GetObject(metaDict.GetAt(ProjectIdRecordName), OpenMode.ForWrite);
                }
                else
                {
                    xRec = new Xrecord();
                    metaDict.SetAt(ProjectIdRecordName, xRec);
                    tr.AddNewlyCreatedDBObject(xRec, true);
                }

                xRec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, projectId));
                tr.Commit();
                
                PluginMain.MyControl?.AddLog($"[PROYECTO] DWG vinculado permanentemente al proyecto: {projectId}");
            }
        }

        public static string GetBoundProjectId(Document doc)
        {
            if (doc == null) return null;

            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                
                if (!nod.Contains(MetaDictionaryName)) return null;

                DBDictionary metaDict = (DBDictionary)tr.GetObject(nod.GetAt(MetaDictionaryName), OpenMode.ForRead);
                
                if (!metaDict.Contains(ProjectIdRecordName)) return null;

                Xrecord xRec = (Xrecord)tr.GetObject(metaDict.GetAt(ProjectIdRecordName), OpenMode.ForRead);
                
                if (xRec.Data != null)
                {
                    var typedValues = xRec.Data.AsArray();
                    if (typedValues.Length > 0 && typedValues[0].TypeCode == (short)DxfCode.Text)
                    {
                        return typedValues[0].Value.ToString();
                    }
                }
            }
            return null;
        }
    }
}
