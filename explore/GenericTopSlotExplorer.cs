﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Research.MultiWorldTesting.ExploreLibrary
{
    public class GenericTopSlotExplorerState
    {
        [JsonProperty("p")]
        public float[] Probabilities { get; set; }
    }

    public class GenericTopSlotExplorer : BaseExplorer<int[], ActionProbability[]>
    {
        public override ExplorerDecision<int[]> MapContext(PRG prg, ActionProbability[] actionProbs, int numActions)
        {
            // Create a discrete_distribution based on the returned actionProbs. This class handles the
            // case where the sum of the actionProbs is < or > 1, by normalizing agains the sum.
            float total = 0f;
            foreach (var ap in actionProbs)
            {
                if (ap.Probability < 0)
                    throw new ArgumentException("Probabilities must be non-negative.");

                total += ap.Probability;
            }

            if (total == 0)
                throw new ArgumentException("At least one probability must be positive.");

            if (Math.Abs(total - 1f) > 1e-6)
                throw new ArgumentException("Probabilities must sum to one.");

            float draw = prg.UniformUnitInterval();

            float sum = 0f;
            var actionChosen = actionProbs.Last();
            foreach (var ap in actionProbs)
            {
                sum += ap.Probability;
                if (sum > draw)
                {
                    actionChosen = ap;
                    break;
                }
            }

            // top slot explorer
            var action = actionChosen.Action;
            var probability = actionChosen.Probability;
            var actionList = new int[actionProbs.Length]; 
            var probabilityList = new float[actionProbs.Length];
            for (int i = 0; i < actionList.Length; i++)
            {
                actionList[i] = actionProbs[i].Action;
                probabilityList[i] = actionProbs[i].Probability;

                if (action == actionList[i])
                {
                    // swap both
                    actionList[i] = actionList[0];
                    actionList[0] = action;

                    probabilityList[i] = probabilityList[0];
                    probabilityList[0] = probability;
                }
            }

            // action id is 1-based
            return ExplorerDecision.Create(
                actionList,
                new GenericTopSlotExplorerState { Probabilities = probabilityList },
                true);
        }
    }
}
