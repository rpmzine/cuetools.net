#include "MACLibDll.h"
#include "IO.h"

using namespace APE;

class CallbackCIO : public CIO
{
public:
    // construction / destruction
    CallbackCIO(
        void * pUserData,
        proc_APECIO_Read CIO_Read,
        proc_APECIO_Write CIO_Write,
        proc_APECIO_Seek CIO_Seek,
        proc_APECIO_GetPosition CIO_GetPosition,
        proc_APECIO_GetSize CIO_GetSize)
    {
        m_pUserData = pUserData;
        m_CIO_Read = CIO_Read;
        m_CIO_Write = CIO_Write;
        m_CIO_Seek = CIO_Seek;
        m_CIO_GetPosition = CIO_GetPosition;
        m_CIO_GetSize = CIO_GetSize;
    }
    ~CallbackCIO() {}

    // open / close
    int Open(const wchar_t * pName, bool bOpenReadOnly = false)
    {
        return -1;
    }
    int Close()
    {
        return -1;
    }

    // read / write
    int Read(void * pBuffer, unsigned int nBytesToRead, unsigned int * pBytesRead)
    {
        return m_CIO_Read(m_pUserData, pBuffer, nBytesToRead, pBytesRead);
    }
    int Write(const void * pBuffer, unsigned int nBytesToWrite, unsigned int * pBytesWritten)
    {
        return m_CIO_Write(m_pUserData, pBuffer, nBytesToWrite, pBytesWritten);
    }

    // seek
    int Seek(APE::int64 nDistance, APE::SeekMethod nMoveMode)
    {
        return m_CIO_Seek(m_pUserData, nDistance, nMoveMode);
    }

    // other functions
    int SetEOF()
    {
        return -1;
    }
    unsigned char * GetBuffer(int * pnBufferBytes)
    {
        return 0;
    }

    // creation / destruction
    int Create(const wchar_t * pName)
    {
        return -1;
    }
    int Delete()
    {
        return -1;
    }

    // attributes
    APE::int64 GetPosition()
    {
        return m_CIO_GetPosition(m_pUserData);
    }
    APE::int64 GetSize()
    {
        return m_CIO_GetSize(m_pUserData);
    }
    int GetName(wchar_t * pBuffer)
    {
        return -1;
    }

private:
    void * m_pUserData;
    proc_APECIO_Read m_CIO_Read;
    proc_APECIO_Write m_CIO_Write;
    proc_APECIO_Seek m_CIO_Seek;
    proc_APECIO_GetPosition m_CIO_GetPosition;
    proc_APECIO_GetSize m_CIO_GetSize;
};

int __stdcall GetVersionNumber()
{
    return APE_FILE_VERSION_NUMBER;
}

const wchar_t *__stdcall GetVersionString()
{
    return APE_VERSION_STRING;
}

APE_CIO_HANDLE __stdcall c_APECIO_Create(void* pUserData,
        proc_APECIO_Read CIO_Read,
        proc_APECIO_Write CIO_Write,
        proc_APECIO_Seek CIO_Seek,
        proc_APECIO_GetPosition CIO_GetPosition,
        proc_APECIO_GetSize CIO_GetSize)
{
    return (APE_CIO_HANDLE) new CallbackCIO(pUserData, CIO_Read, CIO_Write, CIO_Seek, CIO_GetPosition, CIO_GetSize);
}

void __stdcall c_APECIO_Destroy(APE_CIO_HANDLE hCIO)
{
    CallbackCIO * pCIO = (CallbackCIO *) hCIO;
    if (pCIO)
        delete pCIO;
}

/**************************************************************************************************
CAPEDecompress wrapper(s)
**************************************************************************************************/
#ifndef EXCLUDE_CIO
APE_DECOMPRESS_HANDLE __stdcall c_APEDecompress_Create(const str_ansi * pFilename, int * pErrorCode)
{
    CSmartPtr<wchar_t> spFilename(CAPECharacterHelper::GetUTF16FromANSI(pFilename), true);
    return static_cast<APE_DECOMPRESS_HANDLE>(CreateIAPEDecompress(spFilename, pErrorCode, true, true, false));
}

APE_DECOMPRESS_HANDLE __stdcall c_APEDecompress_CreateW(const str_utfn * pFilename, int * pErrorCode)
{
    return static_cast<APE_DECOMPRESS_HANDLE>(CreateIAPEDecompress(pFilename, pErrorCode, true, true, false));
}
#endif

APE_DECOMPRESS_HANDLE __stdcall c_APEDecompress_CreateEx(APE_CIO_HANDLE hCIO, int * pErrorCode)
{
    return static_cast<APE_DECOMPRESS_HANDLE>(CreateIAPEDecompressEx((CallbackCIO *) hCIO, pErrorCode));
}

void __stdcall c_APEDecompress_Destroy(APE_DECOMPRESS_HANDLE hAPEDecompress)
{
    IAPEDecompress * pAPEDecompress = static_cast<IAPEDecompress *>(hAPEDecompress);
    if (pAPEDecompress)
        delete pAPEDecompress;
}

int __stdcall c_APEDecompress_GetData(APE_DECOMPRESS_HANDLE hAPEDecompress, unsigned char * pBuffer, APE::int64 nBlocks, APE::int64 * pBlocksRetrieved)
{
    return (static_cast<IAPEDecompress *>(hAPEDecompress))->GetData(pBuffer, nBlocks, pBlocksRetrieved);
}

int __stdcall c_APEDecompress_Seek(APE_DECOMPRESS_HANDLE hAPEDecompress, APE::int64 nBlockOffset)
{
    return (static_cast<IAPEDecompress *>(hAPEDecompress))->Seek(nBlockOffset);
}

APE::int64 __stdcall c_APEDecompress_GetInfo(APE_DECOMPRESS_HANDLE hAPEDecompress, IAPEDecompress::APE_DECOMPRESS_FIELDS Field, APE::int64 nParam1, APE::int64 nParam2)
{
    return (static_cast<IAPEDecompress *>(hAPEDecompress))->GetInfo(Field, nParam1, nParam2);
}

/**************************************************************************************************
CAPECompress wrapper(s)
**************************************************************************************************/
APE_COMPRESS_HANDLE __stdcall c_APECompress_Create(int * pErrorCode)
{
    return static_cast<APE_COMPRESS_HANDLE>(CreateIAPECompress(pErrorCode));
}

void __stdcall c_APECompress_Destroy(APE_COMPRESS_HANDLE hAPECompress)
{
    IAPECompress * pAPECompress = static_cast<IAPECompress *>(hAPECompress);
    if (pAPECompress)
        delete pAPECompress;
}

#ifndef EXCLUDE_CIO
int __stdcall c_APECompress_Start(APE_COMPRESS_HANDLE hAPECompress, const char * pOutputFilename, const APE::WAVEFORMATEX * pwfeInput, APE::int64 nMaxAudioBytes, int nCompressionLevel, const void * pHeaderData, APE::int64 nHeaderBytes)
{
    CSmartPtr<wchar_t> spOutputFilename(CAPECharacterHelper::GetUTF16FromANSI(pOutputFilename), TRUE);
    return (static_cast<IAPECompress *>(hAPECompress))->Start(spOutputFilename, pwfeInput, false, nMaxAudioBytes, nCompressionLevel, pHeaderData, nHeaderBytes);
}

int __stdcall c_APECompress_StartW(APE_COMPRESS_HANDLE hAPECompress, const str_utfn * pOutputFilename, const APE::WAVEFORMATEX * pwfeInput, APE::int64 nMaxAudioBytes, int nCompressionLevel, const void * pHeaderData, APE::int64 nHeaderBytes)
{
    return (static_cast<IAPECompress *>(hAPECompress))->Start(pOutputFilename, pwfeInput, false, nMaxAudioBytes, nCompressionLevel, pHeaderData, nHeaderBytes);
}
#endif

int __stdcall c_APECompress_StartEx(APE_COMPRESS_HANDLE hAPECompress, APE_CIO_HANDLE hCIO, const APE::WAVEFORMATEX * pwfeInput, APE::int64 nMaxAudioBytes, int nCompressionLevel, const void * pHeaderData, APE::int64 nHeaderBytes)
{
    return (static_cast<IAPECompress *>(hAPECompress))->StartEx((CallbackCIO *) hCIO, pwfeInput, false, nMaxAudioBytes, nCompressionLevel, pHeaderData, nHeaderBytes);
}

APE::int64 __stdcall c_APECompress_AddData(APE_COMPRESS_HANDLE hAPECompress, unsigned char * pData, int nBytes)
{
    return (static_cast<IAPECompress *>(hAPECompress))->AddData(pData, nBytes);
}

int __stdcall c_APECompress_GetBufferBytesAvailable(APE_COMPRESS_HANDLE hAPECompress)
{
    return static_cast<int>((static_cast<IAPECompress *>(hAPECompress))->GetBufferBytesAvailable());
}

unsigned char * __stdcall c_APECompress_LockBuffer(APE_COMPRESS_HANDLE hAPECompress, APE::int64 * pBytesAvailable)
{
    return (static_cast<IAPECompress *>(hAPECompress))->LockBuffer(pBytesAvailable);
}

int __stdcall c_APECompress_UnlockBuffer(APE_COMPRESS_HANDLE hAPECompress, int nBytesAdded, BOOL bProcess)
{
    return static_cast<int>((static_cast<IAPECompress *>(hAPECompress))->UnlockBuffer(nBytesAdded, bProcess));
}

int __stdcall c_APECompress_Finish(APE_COMPRESS_HANDLE hAPECompress, unsigned char * pTerminatingData, APE::int64 nTerminatingBytes, APE::int64 nWAVTerminatingBytes)
{
    return (static_cast<IAPECompress *>(hAPECompress))->Finish(pTerminatingData, nTerminatingBytes, nWAVTerminatingBytes);
}
