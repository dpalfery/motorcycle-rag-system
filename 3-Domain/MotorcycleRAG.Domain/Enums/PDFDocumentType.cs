using System;
using System.Collections.Generic;
using System.Text;

namespace MotorcycleRAG.Domain.Enums
{
    /// <summary>
    /// Types of PDF documents in the motorcycle domain
    /// </summary>
    public enum PDFDocumentType
    {
        Manual,
        ServiceGuide,
        PartsManual,
        OwnerManual,
        TechnicalSpecification,
        RepairGuide,
        Other
    }
}
