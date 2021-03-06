﻿//--------------------------------------------------------------
//
// Browser Efficiency Test
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// MIT License
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files(the ""Software""),
// to deal in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE AUTHORS
// OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF
// OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
//--------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BrowserEfficiencyTest
{
    /// <summary>
    /// Parses the command line arguments and provides access to the various 
    /// arguments and options.
    /// </summary>
    internal class Arguments
    {
        private static readonly List<string> s_SupportedBrowsers = new List<string> { "chrome", "edge", "firefox", "opera" };

        private Dictionary<string, WorkloadScenario> _possibleScenarios;
        private List<WorkloadScenario> _scenarios;
        private List<string> _browsers;
        private Dictionary<string, MeasureSet> _availableMeasureSets;
        private List<MeasureSet> _selectedMeasureSets;
        private List<Workload> _workloads;

        public string ScenarioName { get; private set; }
        public string BrowserProfilePath { get; private set; }
        public bool DoWarmup { get; private set; }
        public int Iterations { get; private set; }
        public bool UsingTraceController { get; private set; }
        public string EtlPath { get; private set; }
        public int MaxAttempts { get; private set; }
        public bool OverrideTimeout { get; private set;  }
        public bool DoPostProcessing { get; private set; }
        public string CredentialPath { get; private set; }

        /// <summary>
        /// List of all scenarios to be run.
        /// </summary>
        public IReadOnlyCollection<WorkloadScenario> Scenarios
        {
            get { return _scenarios.AsReadOnly(); }
        }

        /// <summary>
        /// List of all browsers to be run.
        /// </summary>
        public IReadOnlyCollection<string> Browsers
        {
            get { return _browsers.AsReadOnly(); }
        }

        /// <summary>
        /// List of all measure sets selected to be run.
        /// </summary>
        public IReadOnlyCollection<MeasureSet> SelectedMeasureSets
        {
            get { return _selectedMeasureSets.AsReadOnly(); }
        }

        /// <summary>
        /// Initializes a new instance of the Arguments class and processes the passed in array of command line arguments.
        /// Based on user input in the args, here we break them apart and determine:
        ///  - Which browser to run on
        ///  - Which scenario(s) to run
        ///  - How many loops to execute
        /// </summary>
        /// <param name="args">Array of strings containing the command line arguments.</param>
        public Arguments(string[] args)
        {
            _possibleScenarios = new Dictionary<string, WorkloadScenario>();
            _scenarios = new List<WorkloadScenario>();
            _browsers = new List<string>();
            _availableMeasureSets = PerfProcessor.AvailableMeasureSets.ToDictionary(k => k.Key, v => v.Value);
            _selectedMeasureSets = new List<MeasureSet>();
            _workloads = new List<Workload>();
            ScenarioName = "";
            BrowserProfilePath = "";
            DoWarmup = false;
            Iterations = 1;
            UsingTraceController = false;
            EtlPath = "";
            MaxAttempts = 3;
            OverrideTimeout = false;
            DoPostProcessing = true;
            CredentialPath = "credentials.json";

            CreatePossibleScenarios();
            ProcessWorkloads();
            ProcessArgs(args);
        }

        /// <summary>
        /// Go through the list of passed in commandline 
        /// </summary>
        private void ProcessArgs(string[] args)
        {
            // Processes the arguments. Here we'll decide which browser, scenarios, and number of loops to run
            Console.WriteLine("Usage: BrowserEfficiencyTest.exe [-browser|-b [chrome|edge|firefox|opera|operabeta] -scenario|-s all|<scenario1> <scenario2>] [-iterations|-i <iterationcount>] [-tracecontrolled|-tc <etlpath> -measureset|-ms <measureset1> <measureset2>] [-warmup] [-profile|-p <chrome profile path>] [-attempts|-a <attempts to make per iteration>] [-notimeout] [-noprocessing|-np][-workload|-w <workload name>] [-credentialpath|-cp <path to credentials json file>]");
            for (int argNum = 0; argNum < args.Length; argNum++)
            {
                var arg = args[argNum].ToLowerInvariant();
                switch (arg)
                {
                    case "-browser":
                    case "-b":
                        argNum++;

                        if (args[argNum].ToLowerInvariant() == "all")
                        {
                            foreach (string browser in s_SupportedBrowsers)
                            {
                                _browsers.Add(browser);
                            }

                            break;
                        }

                        while (argNum < args.Length)
                        {
                            string browser = args[argNum].ToLowerInvariant();
                            if (!s_SupportedBrowsers.Contains(browser))
                            {
                                throw new Exception($"Unsupported browser '{browser}'");
                            }

                            _browsers.Add(browser);
                            int nextArgNum = argNum + 1;
                            if (nextArgNum < args.Length && args[argNum + 1].StartsWith("-"))
                            {
                                break;
                            }

                            argNum++;
                        }

                        break;
                    case "-workload":
                    case "-w":
                        argNum++;
                        AddScenariosInWorkload(args[argNum]);
                        break;
                    case "-scenario":
                    case "-s":
                        argNum++;

                        while (argNum < args.Length)
                        {
                            var scenario = args[argNum];
                            if (!_possibleScenarios.ContainsKey(scenario))
                            {
                                throw new Exception($"Unexpected scenario '{scenario}'");
                            }

                            _scenarios.Add(_possibleScenarios[scenario]);

                            if (string.IsNullOrEmpty(ScenarioName))
                            {
                                ScenarioName = scenario;
                            }
                            else
                            {
                                //ScenarioName = ScenarioName + "_" + scenario;
                                ScenarioName = ScenarioName + "-" + scenario;
                            }

                            int nextArgNum = argNum + 1;
                            if (nextArgNum < args.Length && args[argNum + 1].StartsWith("-"))
                            {
                                break;
                            }

                            argNum++;
                        }

                        break;
                    case "-tracecontrolled":
                    case "-tc":
                        UsingTraceController = true;
                        argNum++;
                        string etlPath = args[argNum];

                        if (!Directory.Exists(etlPath))
                        {
                            Directory.CreateDirectory(etlPath);
                        }

                        EtlPath = Path.GetFullPath(etlPath);

                        break;
                    case "-measureset":
                    case "-ms":
                        argNum++;

                        while (argNum < args.Length)
                        {
                            var measureSet = args[argNum];
                            if (!_availableMeasureSets.ContainsKey(measureSet))
                            {
                                throw new Exception($"Unexpected measure set '{measureSet}'");
                            }

                            _selectedMeasureSets.Add(_availableMeasureSets[measureSet]);

                            int nextArgNum = argNum + 1;
                            if (nextArgNum < args.Length && args[argNum + 1].StartsWith("-"))
                            {
                                break;
                            }

                            argNum++;
                        }

                        break;
                    case "-warmup":
                        DoWarmup = true;
                        break;
                    case "-iterations":
                    case "-i":
                        argNum++;
                        Iterations = int.Parse(args[argNum]);
                        break;
                    case "-attempts":
                    case "-a":
                        argNum++;
                        MaxAttempts = int.Parse(args[argNum]);
                        break;
                    case "-profile":
                    case "-p":
                        argNum++;
                        BrowserProfilePath = args[argNum];
                        if (!Directory.Exists(BrowserProfilePath))
                        {
                            throw new DirectoryNotFoundException("The profile path does not exist!");
                        }
                        break;
                    case "-notimeout":
                        OverrideTimeout = true;
                        break;
                    case "-noprocessing":
                    case "-np":
                        DoPostProcessing = false;
                        break;
                    case "-credentialpath":
                    case "-cp":
                        argNum++;
                        CredentialPath = args[argNum];
                        break;
                    default:
                        throw new Exception($"Unexpected argument encountered '{args[argNum]}'");
                }
            }

            // For perf processing, ensure that both the tracing controller option and measuresets have been selected
            if (UsingTraceController && _selectedMeasureSets.Count == 0)
            {
                throw new Exception("A measure set must be specified when using a trace controller.");
            }
            else if (!UsingTraceController && _selectedMeasureSets.Count > 0)
            {
                throw new Exception("The tracing controller option must be specified when using measure sets.");
            }
        }

        /// <summary>
        /// All scenarios must be instantiated and added to the list of possible scenarios in this method.
        /// The order doensn't matter.
        /// </summary>
        private void CreatePossibleScenarios()
        {
            // All scenarios are added to the list even if they're not final / not great. Order doesn't matter here.
            AddScenario(new FacebookNewsfeedScroll());
            AddScenario(new GmailGoThroughEmails());
            AddScenario(new Msn());
            AddScenario(new Msnbc());
            AddScenario(new WikipediaUnitedStates());
            AddScenario(new YoutubeWatchVideo());
            AddScenario(new AmazonSearch());
            AddScenario(new GoogleSearch());
            AddScenario(new CnnTopStory());
            AddScenario(new TechRadarSurfacePro4Review());
            AddScenario(new YahooNews());
            AddScenario(new BbcNews());
            AddScenario(new CnnOneStory());
            AddScenario(new FastScenario());
            AddScenario(new AzureDashboard());
            AddScenario(new OutlookOffice());
            AddScenario(new OutlookEmail());
            AddScenario(new PowerBIBrowse());
            AddScenario(new OfficePowerpoint());
            AddScenario(new AboutBlank());
            AddScenario(new OfficeLauncher());
            AddScenario(new YelpSeattleDinner());
            AddScenario(new ZillowSearch());
            AddScenario(new EspnHomepage());
            AddScenario(new LinkedInSatya());
            AddScenario(new TwitterPublic());
            AddScenario(new TumblrTrending());
            AddScenario(new InstagramNYPL());
            AddScenario(new PinterestExplore());
        }

        private void AddScenario(Scenario scenario)
        {
            _possibleScenarios.Add(scenario.Name, new WorkloadScenario(scenario.Name, "new", scenario.DefaultDuration, scenario));
        }

        private void ProcessWorkloads()
        {
            string jsonText = File.ReadAllText("workloads.json");
            _workloads = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Workload>>(jsonText);
        }

        private void AddScenariosInWorkload(string workloadName)
        {
            foreach (Workload workload in _workloads)
            {
                if (workload.Name == workloadName)
                {
                    foreach (WorkloadScenario scenario in workload.Scenarios)
                    {
                        int runtimeDuration = _possibleScenarios[scenario.ScenarioName].Duration;
                        if (scenario.Duration > 0)
                        {
                            runtimeDuration = scenario.Duration;
                        }
                        _scenarios.Add(new WorkloadScenario(scenario.ScenarioName, scenario.Tab, runtimeDuration, _possibleScenarios[scenario.ScenarioName].Scenario));
                    }
                    break;
                }
            }
        }
    }
}
