#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2019 empira Software GmbH, Cologne Area (Germany)
//
// http://www.pdfsharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Internal;
#if !NETFX_CORE && !UWP
using System.Security.Cryptography;
#endif

#pragma warning disable 0169
#pragma warning disable 0649

namespace PdfSharp.Pdf.Security
{
    /// <summary>
    /// Represents the standard PDF security handler.
    /// </summary>
    public sealed class PdfStandardSecurityHandler : PdfSecurityHandler
    {
        internal PdfStandardSecurityHandler(PdfDocument document)
            : base(document)
        { }

        internal PdfStandardSecurityHandler(PdfDictionary dict)
            : base(dict)
        { }

        /// <summary>
        /// Sets the user password of the document. Setting a password automatically sets the
        /// PdfDocumentSecurityLevel to PdfDocumentSecurityLevel.Encrypted128Bit if its current
        /// value is PdfDocumentSecurityLevel.None.
        /// </summary>
        public string UserPassword
        {
            set
            {
                if (_document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.None)
                    _document._securitySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
                _userPassword = value;
            }
        }
        internal string _userPassword;

        /// <summary>
        /// Sets the owner password of the document. Setting a password automatically sets the
        /// PdfDocumentSecurityLevel to PdfDocumentSecurityLevel.Encrypted128Bit if its current
        /// value is PdfDocumentSecurityLevel.None.
        /// </summary>
        public string OwnerPassword
        {
            set
            {
                if (_document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.None)
                    _document._securitySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
                _ownerPassword = value;
            }
        }
        internal string _ownerPassword;

        /// <summary>
        /// Gets or sets the user access permission represented as an integer in the P key.
        /// </summary>
        internal PdfUserAccessPermission Permission
        {
            get
            {
                var permission = (PdfUserAccessPermission)Elements.GetUInteger(Keys.P);
                if (permission == 0u)
                    permission = PdfUserAccessPermission.PermitAll;
                return permission;
            }
            set { Elements.SetUInteger(Keys.P, (uint)value); }
        }

        /// <summary>
        /// Decrypts the whole document.
        /// </summary>
        public void DecryptDocument()
        {
            foreach (PdfReference iref in _document._irefTable.AllReferences)
            {
                if (iref.ObjectID != this.ObjectID)
                    DecryptObject(iref.Value);
            }
        }

        /// <summary>
        /// Decrypts an indirect object.
        /// </summary>
        internal void DecryptObject(PdfObject value)
        {
            Debug.Assert(value.Reference != null);

            SetHashKey(value.ObjectID);
#if DEBUG
            if (value.ObjectID.ObjectNumber == 10)
                GetType();
#endif

            if (value is PdfDictionary dict)
                DecryptDictionary(dict);
            else if (value is PdfArray array)
                DecryptArray(array);
            else if (value is PdfStringObject str)
            {
                if (str.Length != 0)
                {
                    str.EncryptionValue = DecryptBytes(str.EncryptionValue);
                }
            }
        }

        /// <summary>
        /// Decrypts a dictionary.
        /// </summary>
        void DecryptDictionary(PdfDictionary dict)
        {
            // The Cross-Reference stream is not encrypted.
            if (dict.Elements.GetName("/Type") == "/XRef") return;

            foreach (KeyValuePair<string, PdfItem> item in dict.Elements)
            {
                if (item.Value is PdfString value1)
                    DecryptString(value1);
                else if (item.Value is PdfDictionary value2)
                    DecryptDictionary(value2);
                else if (item.Value is PdfArray value3)
                    DecryptArray(value3);
            }
            if (dict.Stream != null && dict.Stream.Value.Length != 0)
            {
                dict.Stream.Value = DecryptBytes(dict.Stream.Value);
            }
        }

        /// <summary>
        /// Decrypts an array.
        /// </summary>
        void DecryptArray(PdfArray array)
        {
            int count = array.Elements.Count;
            for (int idx = 0; idx < count; idx++)
            {
                PdfItem item = array.Elements[idx];
                if (item is PdfString value1)
                    DecryptString(value1);
                else if (item is PdfDictionary value2)
                    DecryptDictionary(value2);
                else if (item is PdfArray value3)
                    DecryptArray(value3);
            }
        }

        /// <summary>
        /// Decrypt a string.
        /// </summary>
        void DecryptString(PdfString value)
        {
            if (value.Length != 0)
            {
                value.EncryptionValue = DecryptBytes(value.EncryptionValue);
            }
        }

        /// <summary>
        /// Encrypts an array.
        /// </summary>
        internal byte[] EncryptBytes(byte[] bytes)
        {
            if (bytes != null && bytes.Length != 0)
            {
                if (_document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.Encrypted128BitAes)
                {
                    return EncryptAes(bytes);
                }
                else
                {
                    PrepareRC4Key();
                    EncryptRC4(bytes);
                    return bytes;
                }
            }
            return bytes;
        }

        private byte[] DecryptBytes(byte[] bytes)
        {
            if (bytes != null && bytes.Length != 0)
            {
                if (_document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.Encrypted128BitAes)
                {
                    return DecryptAes(bytes);
                }
                else
                {
                    // RC4 decryption is equivalent to RC4 encryption
                    PrepareRC4Key();
                    EncryptRC4(bytes);
                    return bytes;
                }
            }
            return bytes;
        }

        #region Encryption Algorithms

        /// <summary>
        /// Checks the password.
        /// </summary>
        /// <param name="inputPassword">Password or null if no password is provided.</param>
        public PasswordValidity ValidatePassword(string inputPassword)
        {
            // We can handle 40 and 128 bit standard encryption.
            string filter = Elements.GetName(PdfSecurityHandler.Keys.Filter);
            int v = Elements.GetInteger(PdfSecurityHandler.Keys.V);
            if (filter != "/Standard" || !(v >= 1 && v <= 4))
                throw new PdfReaderException(PSSR.UnknownEncryption);

            byte[] documentID = PdfEncoders.RawEncoding.GetBytes(Owner.Internals.FirstDocumentID);
            byte[] oValue = PdfEncoders.RawEncoding.GetBytes(Elements.GetString(Keys.O));
            byte[] uValue = PdfEncoders.RawEncoding.GetBytes(Elements.GetString(Keys.U));
            uint pValue = Elements.GetUInteger(Keys.P);
            int rValue = Elements.GetInteger(Keys.R);

            if (inputPassword == null)
                inputPassword = "";

            bool strongEncryption;
            int keyLength;
            switch (rValue)
            {
                case 2:
                    strongEncryption = false;
                    keyLength = 5;
                    break;
                case 3:
                    strongEncryption = true;
                    keyLength = Elements.GetInteger(Keys.Length) / 8;
                    break;
                case 4:
                    CryptFilterDictionary cryptFilter = new CryptFilterDictionary(Elements.GetDictionary(Keys.CF).Elements.GetDictionary("/StdCF"));
                    if (cryptFilter.CFM != CFM.V2 && cryptFilter.CFM != CFM.AESV2 && cryptFilter.AuthEvent != AuthEvent.DocOpen)
                        throw new PdfReaderException(PSSR.UnsupportedCryptFilter);

                    strongEncryption = true;
                    keyLength = cryptFilter.Length;
                    if (cryptFilter.CFM == CFM.AESV2)
                        _document.SecuritySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128BitAes;
                    break;
                default:
                    throw new PdfReaderException(PSSR.UnsupportedRevisionNumber);
            }

            // Try owner password first.
            //byte[] password = PdfEncoders.RawEncoding.GetBytes(inputPassword);
            InitWithOwnerPassword(documentID, inputPassword, oValue, pValue, strongEncryption);
            if (EqualsKey(uValue, keyLength))
            {
                _document.SecuritySettings._hasOwnerPermissions = true;
                return PasswordValidity.OwnerPassword;
            }
            _document.SecuritySettings._hasOwnerPermissions = false;

            // Now try user password.
            //password = PdfEncoders.RawEncoding.GetBytes(inputPassword);
            InitWithUserPassword(documentID, inputPassword, oValue, pValue, strongEncryption);
            if (EqualsKey(uValue, keyLength))
                return PasswordValidity.UserPassword;
            return PasswordValidity.Invalid;
        }

        [Conditional("DEBUG")]
        static void DumpBytes(string tag, byte[] bytes)
        {
            string dump = tag + ": ";
            for (int idx = 0; idx < bytes.Length; idx++)
                dump += String.Format("{0:X2}", bytes[idx]);
            Debug.WriteLine(dump);
        }

        /// <summary>
        /// Pads a password to a 32 byte array.
        /// </summary>
        static byte[] PadPassword(string password)
        {
            byte[] padded = new byte[32];
            if (password == null)
                Array.Copy(PasswordPadding, 0, padded, 0, 32);
            else
            {
                int length = password.Length;
                Array.Copy(PdfEncoders.RawEncoding.GetBytes(password), 0, padded, 0, Math.Min(length, 32));
                if (length < 32)
                    Array.Copy(PasswordPadding, 0, padded, length, 32 - length);
            }
            return padded;
        }
        static readonly byte[] PasswordPadding = // 32 bytes password padding defined by Adobe
            {
              0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
              0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
            };

        /// <summary>
        /// Generates the user key based on the padded user password.
        /// </summary>
        void InitWithUserPassword(byte[] documentID, string userPassword, byte[] ownerKey, uint permissions, bool strongEncryption)
        {
            InitEncryptionKey(documentID, PadPassword(userPassword), ownerKey, permissions, strongEncryption);
            SetupUserKey(documentID);
        }

        /// <summary>
        /// Generates the user key based on the padded owner password.
        /// </summary>
        void InitWithOwnerPassword(byte[] documentID, string ownerPassword, byte[] ownerKey, uint permissions, bool strongEncryption)
        {
            byte[] userPad = ComputeOwnerKey(ownerKey, PadPassword(ownerPassword), strongEncryption);
            InitEncryptionKey(documentID, userPad, ownerKey, permissions, strongEncryption);
            SetupUserKey(documentID);
        }

        /// <summary>
        /// Computes the padded user password from the padded owner password.
        /// </summary>
        byte[] ComputeOwnerKey(byte[] userPad, byte[] ownerPad, bool strongEncryption)
        {
            byte[] ownerKey = new byte[32];
            //#if !SILVERLIGHT
            byte[] digest = _md5.ComputeHash(ownerPad);
            if (strongEncryption)
            {
                byte[] mkey = new byte[16];
                // Hash the pad 50 times
                for (int idx = 0; idx < 50; idx++)
                    digest = _md5.ComputeHash(digest);
                Array.Copy(userPad, 0, ownerKey, 0, 32);
                // Encrypt the key
                for (int i = 0; i < 20; i++)
                {
                    for (int j = 0; j < mkey.Length; ++j)
                        mkey[j] = (byte)(digest[j] ^ i);
                    PrepareRC4Key(mkey);
                    EncryptRC4(ownerKey);
                }
            }
            else
            {
                PrepareRC4Key(digest, 0, 5);
                EncryptRC4(userPad, ownerKey);
            }
            //#endif
            return ownerKey;
        }

        /// <summary>
        /// Computes the encryption key.
        /// </summary>
        void InitEncryptionKey(byte[] documentID, byte[] userPad, byte[] ownerKey, uint permissions, bool strongEncryption)
        {
            //#if !SILVERLIGHT
            _ownerKey = ownerKey;
            _encryptionKey = new byte[strongEncryption ? 16 : 5];

#if !NETFX_CORE && !DNC10
            _md5.Initialize();
            _md5.TransformBlock(userPad, 0, userPad.Length, userPad, 0);
            _md5.TransformBlock(ownerKey, 0, ownerKey.Length, ownerKey, 0);

            // Split permission into 4 bytes
            byte[] permission = new byte[4];
            permission[0] = (byte)permissions;
            permission[1] = (byte)(permissions >> 8);
            permission[2] = (byte)(permissions >> 16);
            permission[3] = (byte)(permissions >> 24);
            _md5.TransformBlock(permission, 0, 4, permission, 0);
            _md5.TransformBlock(documentID, 0, documentID.Length, documentID, 0);
            _md5.TransformFinalBlock(permission, 0, 0);
            byte[] digest = _md5.Hash;
            _md5.Initialize();
            // Create the hash 50 times (only for 128 bit)
            if (_encryptionKey.Length == 16)
            {
                for (int idx = 0; idx < 50; idx++)
                {
                    digest = _md5.ComputeHash(digest);
                    _md5.Initialize();
                }
            }
            Array.Copy(digest, 0, _encryptionKey, 0, _encryptionKey.Length);
            //#endif
#endif
        }

        /// <summary>
        /// Computes the user key.
        /// </summary>
        void SetupUserKey(byte[] documentID)
        {
#if !NETFX_CORE && !DNC10
            //#if !SILVERLIGHT
            if (_encryptionKey.Length == 16)
            {
                _md5.TransformBlock(PasswordPadding, 0, PasswordPadding.Length, PasswordPadding, 0);
                _md5.TransformFinalBlock(documentID, 0, documentID.Length);
                byte[] digest = _md5.Hash;
                _md5.Initialize();
                Array.Copy(digest, 0, _userKey, 0, 16);
                for (int idx = 16; idx < 32; idx++)
                    _userKey[idx] = 0;
                //Encrypt the key
                for (int i = 0; i < 20; i++)
                {
                    for (int j = 0; j < _encryptionKey.Length; j++)
                        digest[j] = (byte)(_encryptionKey[j] ^ i);
                    PrepareRC4Key(digest, 0, _encryptionKey.Length);
                    EncryptRC4(_userKey, 0, 16);
                }
            }
            else
            {
                PrepareRC4Key(_encryptionKey);
                EncryptRC4(PasswordPadding, _userKey);
            }
            //#endif
#endif
        }

        /// <summary>
        /// Prepare the encryption key.
        /// </summary>
        void PrepareRC4Key()
        {
            if (_key != null && _keySize > 0) //!!!mod 2017-11-06 Added "if" because PrepareRC4Key fails if _key is null. But _key appears to be always null, so maybe PrepareKey() is obsolete.
            PrepareRC4Key(_key, 0, _keySize);
        }

        /// <summary>
        /// Prepare the encryption key.
        /// </summary>
        void PrepareRC4Key(byte[] key)
        {
            PrepareRC4Key(key, 0, key.Length);
        }

        /// <summary>
        /// Prepare the encryption key.
        /// </summary>
        void PrepareRC4Key(byte[] key, int offset, int length)
        {
            int idx1 = 0;
            int idx2 = 0;
            for (int idx = 0; idx < 256; idx++)
                _state[idx] = (byte)idx;
            byte tmp;
            for (int idx = 0; idx < 256; idx++)
            {
                idx2 = (key[idx1 + offset] + _state[idx] + idx2) & 255;
                tmp = _state[idx];
                _state[idx] = _state[idx2];
                _state[idx2] = tmp;
                idx1 = (idx1 + 1) % length;
            }
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        // ReSharper disable InconsistentNaming
        void EncryptRC4(byte[] data)
        // ReSharper restore InconsistentNaming
        {
            EncryptRC4(data, 0, data.Length, data);
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        // ReSharper disable InconsistentNaming
        void EncryptRC4(byte[] data, int offset, int length)
        // ReSharper restore InconsistentNaming
        {
            EncryptRC4(data, offset, length, data);
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        void EncryptRC4(byte[] inputData, byte[] outputData)
        {
            EncryptRC4(inputData, 0, inputData.Length, outputData);
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        void EncryptRC4(byte[] inputData, int offset, int length, byte[] outputData)
        {
            length += offset;
            int x = 0, y = 0;
            byte b;
            for (int idx = offset; idx < length; idx++)
            {
                x = (x + 1) & 255;
                y = (_state[x] + y) & 255;
                b = _state[x];
                _state[x] = _state[y];
                _state[y] = b;
                outputData[idx] = (byte)(inputData[idx] ^ _state[(_state[x] + _state[y]) & 255]);
            }
        }

        /// <summary>
        /// Encrypts the data and returns the result, which will be larger than the original data.
        /// </summary>
        byte[] EncryptAes(byte[] data)
        {
            using (Rijndael aes = Rijndael.Create())
            {
                // Settings defined in PDF 32000 spec
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.BlockSize = 128; // 16 bytes
                aes.KeySize = _keySize * 8;
                // Enable for debugging only! Provides a consistent IV when testing the encryption
                // aes.IV = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
                using (ICryptoTransform encryptor = aes.CreateEncryptor(_key, aes.IV))
                {
                    byte[] encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
                    byte[] result = new byte[aes.IV.Length + encrypted.Length];
                    Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                    Array.Copy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
                    return result;
                }
            }
        }

        /// <summary>
        /// Decrypts the data and returns the result, which will be smaller than the encrypted data.
        /// </summary>
        byte[] DecryptAes(byte[] encryptedData)
        {
            using (Rijndael aes = Rijndael.Create())
            {
                // Settings defined in PDF 32000 spec
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.BlockSize = 128; // 16 bytes
                aes.KeySize = _keySize * 8;
                // Retrieve the IV from the encrypted data
                byte[] iv = new byte[16];
                Array.Copy(encryptedData, 0, iv, 0, 16);
                using (ICryptoTransform decryptor = aes.CreateDecryptor(_key, iv))
                {
                    byte[] decrypted = decryptor.TransformFinalBlock(encryptedData, 16, encryptedData.Length - 16);
                    return decrypted;
                }
            }
        }

        /// <summary>
        /// Checks whether the calculated key correct.
        /// </summary>
        bool EqualsKey(byte[] value, int length)
        {
            for (int idx = 0; idx < length; idx++)
            {
                if (_userKey[idx] != value[idx])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Set the hash key for the specified object.
        /// </summary>
        internal void SetHashKey(PdfObjectID id)
        {
#if !NETFX_CORE && !DNC10
            //#if !SILVERLIGHT
            byte[] objectId = new byte[5];
            // Split the object number and generation
            objectId[0] = (byte)id.ObjectNumber;
            objectId[1] = (byte)(id.ObjectNumber >> 8);
            objectId[2] = (byte)(id.ObjectNumber >> 16);
            objectId[3] = (byte)id.GenerationNumber;
            objectId[4] = (byte)(id.GenerationNumber >> 8);
            _md5.Initialize();
            _md5.TransformBlock(_encryptionKey, 0, _encryptionKey.Length, _encryptionKey, 0);
            _md5.TransformBlock(objectId, 0, objectId.Length, objectId, 0);
            if (_document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.Encrypted128BitAes) 
            {
                // Additional padding needed for AES encryption
                byte[] aesPadding = new byte[] { 0x73, 0x41, 0x6C, 0x54 }; // 'sAlT'
                _md5.TransformFinalBlock(aesPadding, 0, aesPadding.Length);
            }
            else
            {
                _md5.TransformFinalBlock(objectId, 0, 0);
            }
            _key = _md5.Hash;
            _md5.Initialize();
            _keySize = _encryptionKey.Length + 5;
            if (_keySize > 16)
                _keySize = 16;
            //#endif
#endif
        }

        /// <summary>
        /// Prepares the security handler for encrypting the document.
        /// </summary>
        public void PrepareEncryption()
        {
            //#if !SILVERLIGHT
            Debug.Assert(_document._securitySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None);
            var permissions = (uint)Permission;
            bool strongEncryption;

            if (_document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.Encrypted128BitAes)
            {
                strongEncryption = true;
                Elements[Keys.V] = new PdfInteger(4);
                Elements[Keys.R] = new PdfInteger(4);
                CryptFilterDictionary aesCryptFilter = new CryptFilterDictionary();
                aesCryptFilter.CFM = CFM.AESV2;
                aesCryptFilter.Length = 16;
                PdfDictionary cryptFilters = new PdfDictionary();
                cryptFilters.Elements["/StdCF"] = aesCryptFilter;
                Elements[Keys.CF] = cryptFilters;
                Elements[Keys.StmF] = new PdfName("/StdCF");
                Elements[Keys.StrF] = new PdfName("/StdCF");
            }
            else if (_document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.Encrypted128Bit)
            {
                strongEncryption = true;
                Elements[Keys.V] = new PdfInteger(2);
                Elements[Keys.Length] = new PdfInteger(128);
                Elements[Keys.R] = new PdfInteger(3);
            }
            else
            {
                strongEncryption = false;
                Elements[Keys.V] = new PdfInteger(1);
                Elements[Keys.Length] = new PdfInteger(40);
                Elements[Keys.R] = new PdfInteger(2);
            }

            if (String.IsNullOrEmpty(_userPassword))
                _userPassword = "";
            // Use user password twice if no owner password provided.
            if (String.IsNullOrEmpty(_ownerPassword))
                _ownerPassword = _userPassword;

            // Correct permission bits
            permissions |= strongEncryption ? 0xfffff0c0 : 0xffffffc0;
            permissions &= 0xfffffffc;

            var pValue = new PdfUInteger(permissions);

            Debug.Assert(_ownerPassword.Length > 0, "Empty owner password.");
            byte[] userPad = PadPassword(_userPassword);
            byte[] ownerPad = PadPassword(_ownerPassword);

            _md5.Initialize();
            _ownerKey = ComputeOwnerKey(userPad, ownerPad, strongEncryption);
            byte[] documentID = PdfEncoders.RawEncoding.GetBytes(_document.Internals.FirstDocumentID);
            InitWithUserPassword(documentID, _userPassword, _ownerKey, permissions, strongEncryption);

            PdfString oValue = new PdfString(PdfEncoders.RawEncoding.GetString(_ownerKey, 0, _ownerKey.Length));
            PdfString uValue = new PdfString(PdfEncoders.RawEncoding.GetString(_userKey, 0, _userKey.Length));

            Elements[Keys.Filter] = new PdfName("/Standard");
            Elements[Keys.O] = oValue;
            Elements[Keys.U] = uValue;
            Elements[Keys.P] = pValue;
            //#endif
        }

        /// <summary>
        /// The global encryption key.
        /// </summary>
        byte[] _encryptionKey;

#if !SILVERLIGHT && !UWP
        /// <summary>
        /// The message digest algorithm MD5.
        /// </summary>
        readonly MD5 _md5 = new MD5CryptoServiceProvider();
#if DEBUG_
        readonly MD5Managed _md5M = new MD5Managed();
#endif
#else
        readonly MD5Managed _md5 = new MD5Managed();
#endif
#if NETFX_CORE
        // readonly MD5Managed _md5 = new MD5Managed();
#endif
        /// <summary>
        /// Bytes used for RC4 encryption.
        /// </summary>
        readonly byte[] _state = new byte[256];

        /// <summary>
        /// The encryption key for the owner.
        /// </summary>
        byte[] _ownerKey = new byte[32];

        /// <summary>
        /// The encryption key for the user.
        /// </summary>
        readonly byte[] _userKey = new byte[32];

        /// <summary>
        /// The encryption key for a particular object/generation.
        /// </summary>
        byte[] _key;

        /// <summary>
        /// The encryption key length for a particular object/generation.
        /// </summary>
        int _keySize;

        #endregion

        internal override void WriteObject(PdfWriter writer)
        {
            // Don't encrypt myself.
            PdfStandardSecurityHandler securityHandler = writer.SecurityHandler;
            writer.SecurityHandler = null;
            base.WriteObject(writer);
            writer.SecurityHandler = securityHandler;
        }

        #region Keys
        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed new class Keys : PdfSecurityHandler.Keys
        {
            /// <summary>
            /// (Required) A number specifying which revision of the standard security handler
            /// should be used to interpret this dictionary:
            /// � 2 if the document is encrypted with a V value less than 2 and does not have any of
            ///   the access permissions set (by means of the P entry, below) that are designated 
            ///   "Revision 3 or greater".
            /// � 3 if the document is encrypted with a V value of 2 or 3, or has any "Revision 3 or 
            ///   greater" access permissions set.
            /// � 4 if the document is encrypted with a V value of 4
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string R = "/R";

            /// <summary>
            /// (Required) A 32-byte string, based on both the owner and user passwords, that is
            /// used in computing the encryption key and in determining whether a valid owner
            /// password was entered.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Required)]
            public const string O = "/O";

            /// <summary>
            /// (Required) A 32-byte string, based on the user password, that is used in determining
            /// whether to prompt the user for a password and, if so, whether a valid user or owner 
            /// password was entered.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Required)]
            public const string U = "/U";

            /// <summary>
            /// (Required) A set of flags specifying which operations are permitted when the document
            /// is opened with user access.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string P = "/P";

            /// <summary>
            /// (Optional; meaningful only when the value of V is 4; PDF 1.5) Indicates whether
            /// the document-level metadata stream is to be encrypted. Applications should respect this value.
            /// Default value: true.
            /// </summary>
            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string EncryptMetadata = "/EncryptMetadata";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            public static DictionaryMeta Meta
            {
                get { return _meta ?? (_meta = CreateMeta(typeof(Keys))); }
            }
            static DictionaryMeta _meta;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta
        {
            get { return Keys.Meta; }
        }
        #endregion
    }
}
