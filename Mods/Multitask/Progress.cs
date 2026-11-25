using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Herblore;
using GamesByMalcs.Melvor2.Entities.Hunting;
using GamesByMalcs.Melvor2.Entities.Thieving;

namespace Multitask;

/// <summary>
/// Three skills work through a longer piece of work in steps, and the game throws those steps away whenever their action
/// stops: a heist's stage (Thieving), a hunt's survey, tracks and tracking steps (Hunting), and how far a potion has got
/// (Herblore: preparing, heating, stirring, bottling). The game stops every running action when another one starts, so
/// each new action used to reset them while Multitask kept them running (reported on Nexus: "Finished steps get lost").
///
/// The progress is noted each time it moves (right after the game's own step, and after a start), and when Multitask starts
/// the same heist, hunt or potion again it is put back where it was. A start by the player is the game's own: nothing is
/// put back then, as without Multitask.
/// </summary>
public static class Progress
{
    abstract record Note(string Target);
    sealed record Heist(string Target, int Stage) : Note(Target);
    sealed record Potion(string Target, HerbloreRecipeState State, int Bottles) : Note(Target);
    sealed record Hunt(string Target, int Tracks, int Step, int Required, HuntState State, HuntingAnimalSpecies? Species, bool SpeciesSet,
        int TrapFails, string Trap, bool Bait, bool FailedOnce, int SurveyFails) : Note(Target);

    static readonly Dictionary<string, Note> s_notes = new();
    static readonly ConditionalWeakTable<ActionComponent, object> s_watched = new();

    /// <summary>True while Multitask starts an action again: the game's own reset in that start is not a step to note.</summary>
    internal static bool Restarting;

    // the game keeps these setters to itself; going through them lets its views hear of the change
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_BottlesRemaining")] static extern void SetBottles(ActiveHerbloreRecipe r, int value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Tracks")] static extern void SetTracks(ActiveHuntingAnimal a, int value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_CurrentStep")] static extern void SetStep(ActiveHuntingAnimal a, int value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_RequiredSteps")] static extern void SetRequired(ActiveHuntingAnimal a, int value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_State")] static extern void SetHuntState(ActiveHuntingAnimal a, HuntState value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_CurrentSpecies")] static extern void SetSpecies(ActiveHuntingAnimal a, HuntingAnimalSpecies? value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_SpeciesSet")] static extern void SetSpeciesSet(ActiveHuntingAnimal a, bool value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_TrapFailCount")] static extern void SetTrapFails(ActiveHuntingAnimal a, int value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_BaitConsumed")] static extern void SetBait(ActiveHuntingAnimal a, bool value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_FailedOnce")] static extern void SetFailedOnce(ActiveHuntingAnimal a, bool value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddOrUpdateFailBonus")] static extern void AddFailBonus(ActiveHuntingAnimal a, Item trap);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_surveyFails")] static extern ref int SurveyFails(ActiveHuntingAnimal a);

    public static Action<string> Log { get; set; } = _ => { };

    /// <summary>A new character: what was noted belongs to the one before.</summary>
    public static void Clear() { lock (s_notes) s_notes.Clear(); }

    /// <summary>The action left Multitask's list: nothing of it is put back any more.</summary>
    public static void Forget(string entity) { lock (s_notes) s_notes.Remove(entity); }

    /// <summary>Notes the progress of a stepped action each time the game moves it on, and when it starts.</summary>
    public static void Watch(ActionComponent action)
    {
        if (action is not (ThievingActionComponent or HerbloreActionComponent or HuntingActionComponent)) return;
        if (s_watched.TryGetValue(action, out _)) return;
        // After the game's own step (added after its handler, so it runs after it). When that step ended the action
        // (the last trap, the last herb, a full bank, a death), the heist, hunt or potion is over: nothing to put back.
        EventHandler moved = (_, _) =>
        {
            if (action.IsStarted) Take(action);
            else Forget(action.Entity.Id);
        };
        if (action.ActionTimer != null) action.ActionTimer.Fired += moved;
        action.Started += (_, _) => Take(action);
        // another heist, animal or potion chosen while it runs: a fresh one, noted as such
        switch (action)
        {
            case ThievingActionComponent t: t.CurrentRecipeChanged += (_, _) => Take(action); break;
            case HerbloreActionComponent h: h.CreatingRecipeChanged += (_, _) => Take(action); break;
            case HuntingActionComponent u: u.CurrentAnimalChanged += (_, _) => Take(action); break;
        }
        s_watched.Add(action, moved);
        Take(action);   // running already (a character just opened): what it has reached so far
    }

    static void Take(ActionComponent action)
    {
        if (!action.IsStarted || Restarting) return;
        try
        {
            Note? note = action switch
            {
                ThievingActionComponent { CurrentRecipe: { } r } => new Heist(r.Recipe.Id, r.CurrentStage),
                HerbloreActionComponent { CreatingRecipe: { } p } => new Potion(p.Recipe.Id, p.State, p.BottlesRemaining),
                HuntingActionComponent { CurrentAnimal: { } a } => new Hunt(a.Animal.Id, a.Tracks, a.CurrentStep, a.RequiredSteps, a.State,
                    a.CurrentSpecies, a.SpeciesSet, a.TrapFailCount, a.SelectedTrap?.Id ?? "", a.BaitConsumed, a.FailedOnce, SurveyFails(a)),
                _ => null,
            };
            // (the offline catch-up runs the game's steps on a thread of its own)
            if (note != null) lock (s_notes) s_notes[action.Entity.Id] = note;
        }
        catch (Exception e) { Log("Multitask: the progress of " + action.Entity.Id + " could not be noted: " + e.Message); }
    }

    /// <summary>
    /// Multitask has just started an action again: the heist, hunt or potion it was busy with goes on from where it was
    /// (only the same one). Returns what was put back, for the window ("" when nothing).
    /// </summary>
    public static string PutBack(ActionComponent action)
    {
        Note? note;
        lock (s_notes) if (!s_notes.TryGetValue(action.Entity.Id, out note)) return "";
        string told = "";
        try
        {
            switch (action)
            {
                case ThievingActionComponent { CurrentRecipe: { } r } when note is Heist h && r.Recipe.Id == h.Target && h.Stage > r.CurrentStage:
                    r.CurrentStage = h.Stage;
                    told = $"heist stage {h.Stage}";
                    break;
                case HerbloreActionComponent herblore when herblore.CreatingRecipe is { } p && note is Potion pot && p.Recipe.Id == pot.Target
                                                          && pot.State != HerbloreRecipeState.Idle
                                                          && (pot.State != p.State || pot.Bottles != p.BottlesRemaining):
                    SetBottles(p, pot.Bottles);
                    p.State = pot.State;
                    herblore.ActionTimer?.StartWithInterval(herblore.GetIntervalForRecipeState(p));
                    told = pot.State.ToString().ToLowerInvariant();
                    break;
                case HuntingActionComponent hunting when hunting.CurrentAnimal is { } a && note is Hunt n && a.Animal.Id == n.Target
                                                        && (n.State is HuntState.Tracking or HuntState.Trapping || n.SurveyFails > 0):
                    SetSpecies(a, n.Species);
                    SetSpeciesSet(a, n.SpeciesSet);
                    SetRequired(a, n.Required);
                    SetStep(a, n.Step);
                    SetTracks(a, n.Tracks);
                    SurveyFails(a) = n.SurveyFails;
                    SetBait(a, n.Bait);
                    SetFailedOnce(a, n.FailedOnce);
                    if (n.TrapFails > 0 && !a.TraplessEnabled && a.SelectedTrap != null && a.SelectedTrap.Id == n.Trap)
                    {
                        SetTrapFails(a, n.TrapFails);
                        AddFailBonus(a, a.SelectedTrap);
                    }
                    SetHuntState(a, n.State);
                    hunting.ActionTimer?.StartWithInterval(a.GetNextInterval());
                    told = n.State == HuntState.Surveying ? "survey" : $"{n.State.ToString().ToLowerInvariant()} {n.Step}/{n.Required}";
                    break;
            }
        }
        catch (Exception e) { Log("Multitask: the progress of " + action.Entity.Id + " could not be put back: " + e.Message); told = ""; }
        Take(action);
        return told;
    }
}
