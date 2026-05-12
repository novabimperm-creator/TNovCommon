using System;
using Autodesk.Revit.DB;

namespace TNovCommon
{
    public static class BoundingBoxXYZExtensions
    {
        public static BoundingBoxXYZ _BbUnion(this BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
        {
            return new BoundingBoxXYZ
            {
                Min = new XYZ(
                        Math.Min(bb1.Min.X, bb2.Min.X),
                        Math.Min(bb1.Min.Y, bb2.Min.Y),
                        Math.Min(bb1.Min.Z, bb2.Min.Z)),
                Max = new XYZ(
                        Math.Max(bb1.Max.X, bb2.Max.X),
                        Math.Max(bb1.Max.Y, bb2.Max.Y),
                        Math.Max(bb1.Max.Z, bb2.Max.Z))
            };
        }

    }
}
