﻿namespace MptUnity.Audio
{
    
    public enum EAudioPlaybackState
    {
        // Keep eStopped at 0 so that it is the default state.
        eStopped = 0,
        ePlaying,
        ePaused,
    }
    
    public interface IAudioSource
    {
        

        EAudioPlaybackState GetPlaybackState();

        // void SetPlaybackState(AudioPlaybackState state);
        
        // All of the following are just for convenience's sake.
        // Playback can be set from state.
        bool IsPaused();
        bool IsPlaying();
        bool IsStopped();

        void Pause();
        void Play();
        void Stop();

    }
}