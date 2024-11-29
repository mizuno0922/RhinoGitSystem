using System;
using Rhino.Geometry;

namespace RhinoGitSystem.Models
{
    public class ObjectChange
    {
        public Guid Id { get; set; }
        public string ChangeType { get; set; } // "Added", "Deleted", "Modified"
        public string SerializedGeometry { get; set; }
        public Transform Transform { get; set; }
    }
}
