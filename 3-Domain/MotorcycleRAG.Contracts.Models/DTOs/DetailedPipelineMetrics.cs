using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Execution metrics
/// </summary>
public class ExecutionMetrics
{
    public int TotalExecutions { get; set; }

    public int SuccessfulExecutions { get; set; }
}

