#pragma once

#include "MACFileArray.h"
#include <thread>

class CMACProcessFiles
{
public:
    CMACProcessFiles();
    virtual ~CMACProcessFiles();

    BOOL Process(MAC_FILE_ARRAY * paryFiles);
    BOOL ProcessFile(int nIndex);

    BOOL UpdateProgress(double dPercentageDone);

    BOOL Pause(BOOL bPause);
    BOOL Stop(BOOL bImmediately);

    inline BOOL GetPaused() const { return m_bPaused; }
    inline BOOL GetStopped() const { return m_bStopped; }
    int GetPausedTotalMS() const;

protected:
    // helpers
    void Destroy();

    // data
    MAC_FILE_ARRAY * m_paryFiles;
    BOOL m_bStopped;
    BOOL m_bPaused;
    unsigned long long m_nPausedStartTickCount;
    APE::int64 m_nPausedTotalMS;

    // thread for processing
    static void ProcessFileThread(MAC_FILE * pInfo);
};
