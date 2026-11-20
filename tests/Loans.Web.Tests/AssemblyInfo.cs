// Program uses Serilog's static bootstrap logger, which is frozen once per host; running hosts
// in parallel races on Log.Logger. Test classes run sequentially (each still gets its own DB).
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
