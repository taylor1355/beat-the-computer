﻿using BeatTheComputer.Core;

using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;

namespace BeatTheComputer.AI.MCTS
{
    class MCTSExampleGenerator
    {
        private const string EXAMPLE_FILE_EXTENSION = ".example";

        private Random rand;

        private MCTS evaluator;
        private GameSettings gameSettings;

        public MCTSExampleGenerator(MCTS evaluator, GameSettings gameSettings)
        {
            rand = new Random();

            this.evaluator = evaluator;
            this.gameSettings = gameSettings;
        }

        // save mappings from sets of game features to mcts evaluations to a file
        public void generateExamples(int numExamples, string exampleDir, bool append)
        {
            Dictionary<double[], double> examples;
            string exampleFile = exampleDir + "\\examples" + EXAMPLE_FILE_EXTENSION;

            string[] existingExampleFiles = Directory.GetFiles(exampleDir, "*" + EXAMPLE_FILE_EXTENSION);
            if (existingExampleFiles.Length > 0 && append) {
                mergeExamples(exampleFile, existingExampleFiles);
                foreach (string file in existingExampleFiles) {
                    if (!file.Equals(exampleFile)) {
                        File.Delete(file);
                    }
                }
                examples = readExamples(exampleFile);
            } else {
                examples = new Dictionary<double[], double>(numExamples, new FeatureArrayComparer());
            }

            Thread[] threads = new Thread[Environment.ProcessorCount / 2];

            string[] exampleFiles = new string[threads.Length + 1];
            exampleFiles[0] = exampleDir + "\\examples0" + EXAMPLE_FILE_EXTENSION;
            if (examples.Count > 0) {
                writeExamples(examples, exampleFiles[0]);
            }
                
            for (int i = 0; i < threads.Length; i++) {
                int subNumExamples = numExamples / threads.Length;
                if (i == threads.Length - 1) {
                    subNumExamples = numExamples - (threads.Length - 1) * numExamples / threads.Length;
                }
                exampleFiles[i + 1] = exampleDir + "\\examples" + (i + 1).ToString() + EXAMPLE_FILE_EXTENSION;
                int index = i;
                threads[i] = new Thread(() => generateExamplesSingleThreaded(subNumExamples, exampleFiles[index + 1]));
                threads[i].Start();    
            }

            foreach (Thread thread in threads) {
                thread.Join();
            }

            // TODO: make up for duplicate examples here

            mergeExamples(exampleFile, exampleFiles);
        }

        private void generateExamplesSingleThreaded(int numExamples, string exampleFile)
        {
            // TODO: take in initial examples to reduce duplicated effort

            Dictionary<double[], double> examples = new Dictionary<double[], double>(numExamples, new FeatureArrayComparer());

            // each batch should take ~120 seconds
            int batchSize = Math.Max(1, 120000 / (int) evaluator.TimeLimit);
            string backupFile = exampleFile + "_backup" + EXAMPLE_FILE_EXTENSION;

            int examplesAdded = 0;
            while (examplesAdded < numExamples) {
                IGameContext randContext = randomContext();
                double[] features = randContext.featurize();

                double currValue;
                bool featuresContained = examples.TryGetValue(features, out currValue);
                if (!featuresContained) {
                    double label = findScore(randContext);
                    addExample(ref examples, features, label);
                    examplesAdded++;

                    if (examplesAdded % batchSize == 0) {
                        writeExamples(examples, backupFile);
                    }
                }
            }

            writeExamples(examples, exampleFile);
            File.Delete(backupFile);
        }

        public void mergeExamples(string outputFile, params string[] exampleFiles)
        {
            Dictionary<double[], double> examples = new Dictionary<double[], double>(new FeatureArrayComparer());

            foreach (string exampleFile in exampleFiles) {
                Dictionary<double[], double> examplesToMerge = readExamples(exampleFile);
                foreach (KeyValuePair<double[], double> example in examplesToMerge) {
                    addExample(ref examples, example.Key, example.Value);
                }
            }

            writeExamples(examples, outputFile);
        }

        private void writeExamples(Dictionary<double[], double> examples, string exampleFile)
        {
            File.WriteAllText(exampleFile, "");
            using (StreamWriter writer = new StreamWriter(exampleFile)) {
                foreach (KeyValuePair<double[], double> example in examples) {
                    string features = "[";
                    foreach (double feature in example.Key) {
                        features += feature.ToString() + ",";
                    }
                    features = features.Remove(features.Length - 1, 1) + "]";

                    string label = example.Value.ToString();

                    writer.WriteLine(features + ":" + label);
                }
            }
        }

        private Dictionary<double[], double> readExamples(string exampleFile)
        {
            Dictionary<double[], double> examples = new Dictionary<double[], double>(new FeatureArrayComparer());

            if (File.Exists(exampleFile)) {
                using (StreamReader reader = new StreamReader(exampleFile)) {
                    string line = reader.ReadLine();
                    while (line != null) {
                        string strFeatures = line.Split(':')[0];
                        strFeatures = strFeatures.Substring(1, strFeatures.Length - 2);
                        double[] features = Array.ConvertAll(strFeatures.Split(','), Double.Parse);
                        double label = Double.Parse(line.Split(':')[1]);

                        addExample(ref examples, features, label);

                        line = reader.ReadLine();
                    }
                }
            }

            return examples;
        }

        private void addExample(ref Dictionary<double[], double> examples, double[] newFeatures, double newLabel)
        {
            double currLabel;
            bool featuresContained = examples.TryGetValue(newFeatures, out currLabel);
            if (featuresContained) {
                examples[newFeatures] = (currLabel + newLabel) / 2;
            } else {
                examples[newFeatures] = newLabel;
            }
        }

        private double findScore(IGameContext context)
        {
            return Math.Min(1, Math.Max(0, evaluator.evaluateContext(context, CancellationToken.None)));
        }

        private IGameContext randomContext()
        {
            IBehavior randomBehavior = new PlayRandom(new Random(rand.Next()));
            IGameContext startContext = gameSettings.newContext();

            List<IGameContext> history = null;
            startContext.simulate(randomBehavior, randomBehavior.clone(), out history);
            return history[rand.Next(history.Count)];
        }
    }
}