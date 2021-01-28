using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RawMono2RGB
{
    public static class IntegrityChecker
    {

        // Important note: This is not 100% mathematically perfect or anything.
        // It will only detect major corruption. The algorithm I use for calculating acceptable loss is likely imperfect. 
        // Might improve in the future, but this should get us close enough.

        private static UInt16[] acceptableLossCache = new UInt16[UInt16.MaxValue+1];
        private static bool acceptableLosscacheCreated = false;

        public static void BuildIntegrityVerificationAcceptableLossCache()
        {
            double tmpDouble;
            for (int i = 0; i <= UInt16.MaxValue; i++)
            {
                tmpDouble = ((double)i / (double)UInt16.MaxValue);
                acceptableLossCache[i] = (UInt16)Math.Ceiling((double)UInt16.MaxValue * Math.Pow(2, Math.Ceiling(Math.Log(tmpDouble, 2.0d)) - 11));
            }
            acceptableLosscacheCreated = true;
        }

        public static bool VerifyIntegrityUInt16InHalfPrecisionFloat(byte[] reference, byte[] dataToCheck)
        {
            bool integrityCheckFailed = false;
            if (dataToCheck.Length != reference.Length)
            {
                integrityCheckFailed = true;
            }
            UInt16 origValue, reloadedValue;
            UInt16 acceptableLoss;
            double tmpDouble;

            if (acceptableLosscacheCreated)
            {
                for (int k = 0; k < reference.Length; k += 2)
                {
                    origValue = BitConverter.ToUInt16(reference, k);
                    reloadedValue = BitConverter.ToUInt16(dataToCheck, k);
                    acceptableLoss = acceptableLossCache[origValue];
                    if (Math.Abs((double)origValue - (double)reloadedValue) > acceptableLoss)
                    {
                        integrityCheckFailed = true;
                        break;
                    }
                }
            }
            else
            {
                for (int k = 0; k < reference.Length; k += 2)
                {
                    origValue = BitConverter.ToUInt16(reference, k);
                    reloadedValue = BitConverter.ToUInt16(dataToCheck, k);
                    tmpDouble = ((double)origValue / (double)UInt16.MaxValue);
                    acceptableLoss = (UInt16)Math.Ceiling((double)UInt16.MaxValue * Math.Pow(2, Math.Ceiling(Math.Log(tmpDouble, 2.0d)) - 11));
                    if (Math.Abs((double)origValue - (double)reloadedValue) > acceptableLoss)
                    {
                        integrityCheckFailed = true;
                        break;
                    }
                }
            }
            
            return !integrityCheckFailed;
        }
    }
}
