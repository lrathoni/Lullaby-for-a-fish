﻿
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;

using MptUnity.Audio.Behaviour;
using MptUnity.Audio;

using Music;

namespace IO.Behaviour
{
    /// <summary>
    /// Event which gets called whenever the FlutePlayer starts playing a MusicalNote.
    /// First argument is index of the tone in the FlutePlayer's range of notes. 
    /// </summary>
    public class OnPlayerNoteStartEvent : UnityEvent<ENoteColour, MusicalNote> { }
    /// <summary>
    /// Event which gets called whenever the FlutePlayer stops playing a MusicalNote.
    /// First argument is index of the tone in the FlutePlayer's range of notes.
    /// </summary>
    public class OnPlayerNoteStopEvent : UnityEvent<ENoteColour , MusicalNote> { }
    
    /// <summary>
    /// Event which gets trigger immediately when the FlutePlayer receives a NoteCommand.
    /// Warning: the note may be invalid.
    /// Can be used to animate the Flute.
    /// See data available in NoteCommand class for details.
    /// </summary>
    public class OnPlayerNoteCommandReceiveEvent : UnityEvent<FlutePlayer.NoteCommand> {}
    
    /// <summary>
    /// Event which gets trigger when the FlutePlayer cancels a NoteCommand.
    /// The argument is the NoteCommand which was cancelled.
    /// Can be used to animate the Flute.
    /// See data available in NoteCommand class for details.
    /// </summary>
    public class OnPlayerNoteCommandCancelEvent : UnityEvent<FlutePlayer.NoteCommand> { }
    
    /// <summary>
    ///Can be used to animate the Flute.
    /// </summary>
    public class OnPlayerEnterStateEvent : UnityEvent<FlutePlayer.EPlayingState, FlutePlayer> { }

    /// <summary>
    /// A class for the Instrument playing component of the Player.
    /// Watch for notes playing with OnPlayerNoteStartEvent and OnPLayerNoteStopEvent.
    /// </summary>
    public class FlutePlayer : UnityEngine.MonoBehaviour
    {

        #region Serialised data 

        public GameObject instrumentSourceObject;
        
        public KeyCode[] keys;
        public int[] tones;

        /// <summary>
        /// Delay before starting playing the Flute.
        /// </summary>
        public float delayStart = 0.5F;
        /// <summary>
        /// Delay before switching notes while playing.
        /// </summary>
        public float delaySwitch = 0.0F;
        /// <summary>
        /// Delay before stopping playing the note.
        /// </summary>
        public float delayStop = 0.2F;

        /// <summary>
        /// Delay before 'Resting' state.
        /// </summary>
        public float delayRest = 1.0F;

        [Range(0L, 1L)] 
        public double volume = 1L;
        
        #endregion

        #region Unity MonoBehaviour events

        void Awake()
        {
            m_events = new Events();

            m_noteCommandQueue = new List<NoteCommand>();
        }

        void Start()
        {
            Assert.IsTrue(NoteColours.GetNumber() == keys.Length && keys.Length == tones.Length,
                "Keys must of the same length, and correspond to NoteColours.");

            StopVoice();            
            State = EPlayingState.eResting;
            
            SetupAudio();
        }

        void Update()
        {
            UpdateInput();
            
            UpdateCommandQueue();

            UpdateState();
        }

        #endregion
        #region Update routine

        void UpdateInput()
        {
            for (int toneIndex = 0; toneIndex < tones.Length; ++toneIndex)
            {
                KeyCode key = keys[toneIndex];
                if (Input.GetKeyDown(key))
                {
                    AddNoteCommand(new NoteStartDelayedCommand(this, (ENoteColour)toneIndex, Time.time));
                }
                else if (Input.GetKeyUp(key))
                {
                    AddNoteCommand(new NoteStopDelayedCommand(this, (ENoteColour)toneIndex, Time.time));
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>Requires that m_noteCommandQueue be sorted by ascending time of issuing.</remarks>
        void UpdateCommandQueue()
        {
            SortCommandQueue();
            
            while ( m_noteCommandQueue.Count > 0
                && m_noteCommandQueue[0].ShouldExecute(Time.time)
                )
            {
                ExecuteFirstInLineCommand();
            }
        }

        void UpdateState()
        {
            if (State == EPlayingState.eStopped && Time.time > m_timeStopped + delayRest)
            {
                State = EPlayingState.eResting;
            }
        }

        #endregion

        #region Command queue operations

        void AddNoteCommand(NoteCommand command)
        {
            m_noteCommandQueue.Add(command);

            var first = m_noteCommandQueue[0];
            SortCommandQueue();
            // Invoke with the next relevant command!
            if (first != m_noteCommandQueue[0] || first == command)
            {
                m_events.playerNoteCommandReceiveEvent.Invoke(m_noteCommandQueue[0]);            
            }
        }

        void SortCommandQueue()
        {
            // Sort by expected execution time
            m_noteCommandQueue.Sort((c1, c2) =>
                {
                    float interval = c1.GetExecutionTime() - c2.GetExecutionTime();
                    if (interval > 0) return 1;
                    if (interval < 0) return -1;
                    return 0;
                }
            );
            
            //Cancel the commands if necessary:
            // A command should be cancelled if the one which should be executed before
            // has been issued after the first.
            int i = 0;
            while (i < m_noteCommandQueue.Count - 1)
            {
                var current = m_noteCommandQueue[i];
                var next = m_noteCommandQueue[i + 1];
                if (current.ShouldCancel(next))
                {
                    m_noteCommandQueue.RemoveAt(i + 1);
                }
                else
                {
                    ++i;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        void ExecuteFirstInLineCommand()
        {
            Assert.IsTrue(m_noteCommandQueue.Count > 0);
            // requires that m_noteCommandQueue be sorted by ascending order of execution time.
            var first = m_noteCommandQueue[0];
            
            m_noteCommandQueue.RemoveAt(0);
            
            first.Execute();
            
            if (m_noteCommandQueue.Count > 0)
            {
               m_events.playerNoteCommandReceiveEvent.Invoke(m_noteCommandQueue[0]); 
            }
        }

        #endregion
        #region Playing routine

        bool StopNoteNow(ENoteColour colour)
        {
            if (m_playingVoice == -1 || m_playingNoteColour != colour)
            {
                // failure.
                return false;
            }
            // Get the note BEFORE stopping it!
            MusicalNote note = m_instrumentSource.GetNote(m_playingVoice);

            bool success = m_instrumentSource.StopNote(m_playingVoice);
            Assert.IsTrue(success, "Call to StopNoteNow should not fail at this point.");
            
            // resetting voice and toneIndex.
            StopVoice();
            State = EPlayingState.eStopped;
            
            // Notifying the listeners that the note just stopped.
            m_events.playerNoteStopEvent.Invoke(colour, note);
            
            return true;
        }

        /// <summary>
        /// Stop note without changing the state .
        /// To be used to switch between two playing notes. 
        /// </summary>
        /// <param name="colour"></param>
        /// <returns></returns>
        /// <remarks> </remarks>
        bool StopNoteNowKeepState(ENoteColour colour)
        {
            if (m_playingVoice == -1 || m_playingNoteColour != colour)
            {
                // failure.
                return false;
            }
            // Get the note BEFORE stopping it!
            MusicalNote note = m_instrumentSource.GetNote(m_playingVoice);

            bool success = m_instrumentSource.StopNote(m_playingVoice);
            Assert.IsTrue(success, "Call to StopNoteNow should not fail at this point.");
            
            // resetting voice and toneIndex.
            StopVoice();
            
            // Notifying the listeners that the note just stopped.
            m_events.playerNoteStopEvent.Invoke(colour, note);
            
            return true;
        }

        int PlayNoteNow(ENoteColour colour)
        {
            
            // We can't have multiple tones playing in the same voice.
            StopNoteNowKeepState(m_playingNoteColour);
            //
            int tone = tones[(int)colour];
            //
            int voice = m_instrumentSource.PlayNote(new MusicalNote(tone, volume));

            if (voice == -1)
            {
                // failure.
                return voice;
            }

            m_playingVoice = voice;
            m_playingNoteColour = colour;
            State = EPlayingState.ePlaying;

            // Notifying the listeners that a note is being played.
            m_events.playerNoteStartEvent.Invoke(colour, m_instrumentSource.GetNote(voice));
            
            return voice;
        }

        #endregion
        #region State
        public enum EPlayingState
        {
            ePlaying,
            eStopped,
            eGettingReady, // todo: should free us from using NoteCommand event for animations.
            eResting
        }

        public EPlayingState State
        {
            get => m_state;
            private set
            {
                if (value == m_state)
                {
                    return;
                }
                m_state = value;
                if (m_state == EPlayingState.eStopped)
                {
                    m_timeStopped = Time.time;
                }
                // Notify!
                m_events.playerEnterStateEvent.Invoke(m_state, this);
            }
        }
        #endregion

        #region Private utility

        void StopVoice()
        {
            // resetting the playing voice 
            m_playingVoice = -1;
        }
        
        void SetupAudio()
        {
            m_instrumentSource = instrumentSourceObject.GetComponent<IInstrumentSource>();
            
            // Force a number of voices
            // Two might yield better results than only one (hopefully).
            if (m_instrumentSource.NumberVoices < 2)
            {
                m_instrumentSource.NumberVoices = 2; 
            }
        }
        

        #endregion

        #region Events

        class Events
        {
            public readonly OnPlayerNoteStartEvent playerNoteStartEvent;
            public readonly OnPlayerNoteStopEvent playerNoteStopEvent;
            public readonly OnPlayerNoteCommandReceiveEvent playerNoteCommandReceiveEvent;
            public readonly OnPlayerNoteCommandCancelEvent playerNoteCommandCancelEvent;
            public readonly OnPlayerEnterStateEvent playerEnterStateEvent;

            public Events()
            {
                playerNoteStartEvent = new OnPlayerNoteStartEvent();
                playerNoteStopEvent  = new OnPlayerNoteStopEvent();
                playerNoteCommandReceiveEvent  = new OnPlayerNoteCommandReceiveEvent();
                playerNoteCommandCancelEvent = new OnPlayerNoteCommandCancelEvent();
                playerEnterStateEvent = new OnPlayerEnterStateEvent();
            }
        }

        public void AddOnNoteStartListener(UnityAction<ENoteColour, MusicalNote> onNoteStart)
        {
            m_events.playerNoteStartEvent.AddListener(onNoteStart);
        }

        public void RemoveOnNoteStartListener(UnityAction<ENoteColour, MusicalNote> onNoteStart)
        {
            m_events.playerNoteStartEvent.RemoveListener(onNoteStart);
        }

        public void AddOnNoteStopListener(UnityAction<ENoteColour, MusicalNote> onNoteStop)
        {
            m_events.playerNoteStopEvent.AddListener(onNoteStop);
        }

        public void RemoveOnNoteStopListener(UnityAction<ENoteColour, MusicalNote> onNoteStop)
        {
            m_events.playerNoteStopEvent.RemoveListener(onNoteStop);
        }
        
        public void AddOnNoteCommandReceiveListener(UnityAction<NoteCommand> onNoteCommandReceive)
        {
            m_events.playerNoteCommandReceiveEvent.AddListener(onNoteCommandReceive);
        }

        public void RemoveOnNoteCommandReceiveListener(UnityAction<NoteCommand> onNoteCommandReceive)
        {
            m_events.playerNoteCommandReceiveEvent.RemoveListener(onNoteCommandReceive);
        }
        
        public void AddOnNoteCommandCancelListener(UnityAction<NoteCommand> onNoteCommandCancel)
        {
            m_events.playerNoteCommandCancelEvent.AddListener(onNoteCommandCancel);
        }

        public void RemoveOnNoteCommandCancelListener(UnityAction<NoteCommand> onNoteCommandCancel)
        {
            m_events.playerNoteCommandCancelEvent.RemoveListener(onNoteCommandCancel);
        }

        public void AddOnEnterStateListener(UnityAction<EPlayingState, FlutePlayer> onEnterState)
        {
            m_events.playerEnterStateEvent.AddListener(onEnterState);
        }
        
        public void RemoveOnEnterStateListener(UnityAction<EPlayingState, FlutePlayer> onEnterState)
        {
            m_events.playerEnterStateEvent.RemoveListener(onEnterState);
        }
        
        #endregion

        #region Note Commands

        public abstract class NoteCommand
        {
            public enum Kind
            {
                eStart,
                eStop
            }
            protected NoteCommand(FlutePlayer a_owner, ENoteColour aNoteColour, Kind aKind, float aTimeIssued)
            {
                owner = a_owner;
                noteColour = aNoteColour;
                timeIssued = aTimeIssued;
                kind = aKind;
            }
            
            public abstract void Execute();

            /// <summary>
            /// Should the command cancel the other?
            /// </summary>
            /// <param name="other"></param>
            /// <returns></returns>
            public virtual bool ShouldCancel(NoteCommand other)
            {
                return GetExecutionTime() < other.GetExecutionTime()
                       && timeIssued > other.timeIssued;
            }

            /// <summary>
            /// Should the command be executed, now that it is next in line?
            /// </summary>
            /// <returns></returns>
            public bool ShouldExecute(float currentTime)
            {
                return currentTime > GetExecutionTime();
            }

            /// <summary>
            /// Time at which the command should be executed.
            /// Should (probably) only be used for ordering CommandQueue.
            /// </summary>
            /// <returns>Time at which the command should executed</returns>
            public abstract float GetExecutionTime();

            public readonly Kind kind;
            public readonly float timeIssued;
            public readonly ENoteColour noteColour;

            protected readonly FlutePlayer owner;
        }

        class NoteStartCommand : NoteCommand
        {
            protected NoteStartCommand(FlutePlayer owner, ENoteColour noteColour, float timeIssued) 
                : base(owner, noteColour, Kind.eStart, timeIssued)
            {
                
            }

            public override float GetExecutionTime()
            {
                // exactly when it was issued.
                return timeIssued;
            }

            public override bool ShouldCancel(NoteCommand other)
            {
                // only one NoteStart at a time!
                return base.ShouldCancel(other) || other.kind == Kind.eStart;
            }

            public override void Execute()
            {
                int voice = owner.PlayNoteNow(noteColour);
                if (voice != -1)
                {
                    // UnityEngine.Debug.Log($"{voice} Started playing!");
                }
            }

        }

        class NoteStopCommand : NoteCommand
        {
            protected NoteStopCommand(FlutePlayer owner, ENoteColour noteColour, float timeIssued) 
                : base(owner, noteColour, Kind.eStop, timeIssued)
            {
                
            }

            public override bool ShouldCancel(NoteCommand other)
            {
                // Should cancel a NoteStart only if it is commanding the same toneIndex.
                return base.ShouldCancel(other) 
                       && other.kind == Kind.eStart && other.noteColour == noteColour;
            }

            public override float GetExecutionTime()
            {
                return timeIssued;
            }

            public override void Execute()
            {
                bool success = owner.StopNoteNow(noteColour);
                if (success)
                {
                    // UnityEngine.Debug.Log($"Stopped playing!");
                }
            }
        }

        class NoteStartDelayedCommand : NoteStartCommand
        {

            public NoteStartDelayedCommand(FlutePlayer owner, ENoteColour noteColour, float timeIssued)
                : base(owner, noteColour, timeIssued)
            {
            }
            public override float GetExecutionTime()
            {
                float executionTime = base.GetExecutionTime();
                switch (owner.State)
                {
                    case EPlayingState.ePlaying: 
                    case EPlayingState.eStopped: executionTime += owner.delaySwitch; break;
                    case EPlayingState.eResting: executionTime += owner.delayStart; break;
                }
                return executionTime;
            }

        }

        class NoteStopDelayedCommand : NoteStopCommand
        {
            public NoteStopDelayedCommand(FlutePlayer owner, ENoteColour noteColour, float timeIssued)
                : base(owner, noteColour, timeIssued)
            {
            }

            public override float GetExecutionTime()
            {
                return base.GetExecutionTime() + owner.delayStop;
            }

        }
        #endregion
        


        #region Private data 

        IInstrumentSource m_instrumentSource;

        ENoteColour m_playingNoteColour;
        int m_playingVoice;
        
        Events m_events;

        List<NoteCommand> m_noteCommandQueue;

        EPlayingState m_state;
        float m_timeStopped;

        #endregion
    }
}