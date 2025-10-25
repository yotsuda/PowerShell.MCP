// Copyright (c) Yoshifumi Tsuda
// Licensed under the MIT License

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerShell.MCP.Utilities
{
    /// <summary>
    /// Executes PowerShell commands with unified stream capture and real-time console display.
    /// Uses Pipeline API with MergeMyResults for console-order stream integration.
    /// </summary>
    public static class CommandExecutor
    {
        /// <summary>
        /// Result of command execution containing unified output in console order.
        /// </summary>
        public class ExecutionResult
        {
            /// <summary>
            /// Unified output in console order (plain text).
            /// </summary>
            public List<string> Output { get; set; } = new List<string>();
            
            /// <summary>
            /// Execution duration in seconds.
            /// </summary>
            public double DurationSeconds { get; set; }
            
            /// <summary>
            /// Whether any errors occurred during execution.
            /// </summary>
            public bool HadErrors { get; set; }
        }

        /// <summary>
        /// Execute a PowerShell command with full stream capture and real-time console display.
        /// </summary>
        /// <param name="command">PowerShell command to execute</param>
        /// <param name="runspace">Runspace to use for execution (required)</param>
        /// <param name="displayToConsole">Whether to display output to console in real-time (default: true)</param>
        /// <returns>ExecutionResult containing unified output in console order</returns>
        public static ExecutionResult Execute(string command, Runspace runspace, bool displayToConsole = true)
        {
            if (runspace == null)
                throw new ArgumentNullException(nameof(runspace));

            var result = new ExecutionResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Create pipeline using the provided runspace
                Pipeline pipeline = runspace.CreatePipeline();

                // Setup real-time output handler
                pipeline.Output.DataReady += (sender, eventArgs) =>
                {
                    var reader = (PipelineReader<PSObject>)sender;
                    
                    // Read all available objects
                    while (reader.Count > 0)
                    {
                        PSObject obj = reader.Read();
                        
                        // Display to console immediately (with colors)
                        if (displayToConsole)
                        {
                            DisplayToConsole(obj);
                        }
                        
                        // Capture as plain text
                        result.Output.Add(obj.ToString());
                    }
                };

                // Add command
                pipeline.Commands.AddScript(command);
                
                // Merge all streams to output stream (CRITICAL for console order)
                pipeline.Commands[pipeline.Commands.Count - 1]
                    .MergeMyResults(PipelineResultTypes.All, PipelineResultTypes.Output);

                // Execute
                pipeline.Invoke();

                // Check for errors
                result.HadErrors = pipeline.Error.Count > 0;
            }
            catch (RuntimeException ex)
            {
                // PowerShell execution error
                result.HadErrors = true;
                
                var errorMessage = $"PowerShell execution error: {ex.Message}";
                result.Output.Add(errorMessage);
                
                if (displayToConsole)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(errorMessage);
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                // Unexpected error
                result.HadErrors = true;
                
                var errorMessage = $"Command execution failed: {ex.Message}";
                result.Output.Add(errorMessage);
                
                if (displayToConsole)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(errorMessage);
                    Console.ResetColor();
                }
            }
            finally
            {
                stopwatch.Stop();
                result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
            }

            return result;
        }

        /// <summary>
        /// Display a PSObject to console with appropriate color based on stream type.
        /// </summary>
        private static void DisplayToConsole(PSObject obj)
        {
            var baseObj = obj.ImmediateBaseObject;

            switch (baseObj)
            {
                case ErrorRecord errorRecord:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(errorRecord.ToString());
                    Console.ResetColor();
                    break;

                case WarningRecord warningRecord:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"WARNING: {warningRecord.Message}");
                    Console.ResetColor();
                    break;

                case VerboseRecord verboseRecord:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"VERBOSE: {verboseRecord.Message}");
                    Console.ResetColor();
                    break;

                case InformationRecord informationRecord:
                    // Write-Host output comes as InformationRecord
                    Console.WriteLine(informationRecord.MessageData?.ToString() ?? informationRecord.ToString());
                    break;

                case DebugRecord debugRecord:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"DEBUG: {debugRecord.Message}");
                    Console.ResetColor();
                    break;

                default:
                    // Normal output
                    Console.WriteLine(obj.ToString());
                    break;
            }
        }
    }
}
