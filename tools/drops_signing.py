#!/usr/bin/env python3
"""DROPS — chaves de assinatura e assinatura do manifest de update.

Subcomandos:
  gen                     gera um par ECDSA P-256 e imprime as chaves
  sign <manifest> <saida> assina o manifest (bytes exatos do arquivo)
  verify <manifest> <sig> verifica uma assinatura
  pubid <chave-publica>   imprime o kid (id curto) de uma chave pública

Formato da assinatura: base64 dos 64 bytes r||s (raw), que é o que o WebCrypto
do Worker produz e o que o ECDsa do .NET espera em VerifyData. Não use DER aqui.

Uso no release (build.ps1 chama isto):
  python drops_signing.py gen > chaves.txt          # uma vez, guarde fora do repo
  python drops_signing.py sign manifest.json manifest.sig
"""
import base64
import hashlib
import json
import sys

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, utils


def _b64(raw: bytes) -> str:
    return base64.b64encode(raw).decode("ascii")


def _unb64(text: str) -> bytes:
    return base64.b64decode("".join(text.split()), validate=False)


def _kid_from_spki(spki: bytes) -> str:
    return hashlib.sha256(spki).hexdigest()[:16]


def _load_private(private_b64: str):
    return serialization.load_der_private_key(_unb64(private_b64), password=None)


def _load_public(public_b64: str):
    return serialization.load_der_public_key(_unb64(public_b64))


def _spki(public_key) -> bytes:
    return public_key.public_bytes(
        serialization.Encoding.DER,
        serialization.PublicFormat.SubjectPublicKeyInfo,
    )


def _sign_raw(private_key, data: bytes) -> bytes:
    """Assina e devolve r||s (64 bytes), não DER."""
    der = private_key.sign(data, ec.ECDSA(hashes.SHA256()))
    r, s = utils.decode_dss_signature(der)
    return r.to_bytes(32, "big") + s.to_bytes(32, "big")


def _verify_raw(public_key, data: bytes, sig_raw: bytes) -> bool:
    if len(sig_raw) != 64:
        return False
    r = int.from_bytes(sig_raw[:32], "big")
    s = int.from_bytes(sig_raw[32:], "big")
    der = utils.encode_dss_signature(r, s)
    try:
        public_key.verify(der, data, ec.ECDSA(hashes.SHA256()))
        return True
    except Exception:
        return False


def cmd_gen(out_path: str = "") -> int:
    key = ec.generate_private_key(ec.SECP256R1())
    priv = key.private_bytes(
        serialization.Encoding.DER,
        serialization.PrivateFormat.PKCS8,
        serialization.NoEncryption(),
    )
    spki = _spki(key.public_key())
    kid = _kid_from_spki(spki)

    print("# Chaves DROPS geradas. GUARDE FORA DO REPOSITORIO.\n")
    print("# 1) Publique a privada no Worker:")
    print(f"npx wrangler secret put SIGNING_PRIVATE_KEY\n{_b64(priv)}\n")
    print("# 2) Embuta a publica no loader (lista de confianca):")
    print(f'DROPS_PUBKEYS = new[] {{ "{_b64(spki)}" }};')
    print(f"# kid = {kid}\n")
    print("# A publica pode ser divulgada. A privada, nunca.")

    if out_path:
        # Arquivo maquina-legivel para o pipeline de release e para os testes.
        with open(out_path, "w", encoding="utf-8") as fh:
            json.dump({"private_key": _b64(priv), "public_key": _b64(spki), "kid": kid}, fh, indent=2)
        print(f"# chaves tambem salvas em: {out_path}")
    return 0


def cmd_sign(manifest_path: str, out_path: str) -> int:
    private_b64 = _read_key_from_env_or_prompt()
    private_key = _load_private(private_b64)

    # Assina os bytes EXATOS do arquivo. Re-serializar o JSON mudaria os bytes
    # e o cliente verificaria contra algo diferente do que foi publicado.
    with open(manifest_path, "rb") as fh:
        data = fh.read()

    sig = _sign_raw(private_key, data)
    with open(out_path, "w", encoding="ascii") as fh:
        fh.write(_b64(sig))

    print(f"assinado: {manifest_path} -> {out_path}")
    print(f"  sha256(manifest) = {hashlib.sha256(data).hexdigest()}")
    print(f"  kid               = {_kid_from_spki(_spki(private_key.public_key()))}")
    print(f"  bytes assinados   = {len(data)}")
    return 0


def cmd_verify(manifest_path: str, sig_path: str) -> int:
    public_b64 = _read_key_from_env_or_prompt("DROPS_PUBLIC_KEY")
    public_key = _load_public(public_b64)

    with open(manifest_path, "rb") as fh:
        data = fh.read()
    with open(sig_path, "r", encoding="ascii") as fh:
        sig = _unb64(fh.read())

    if _verify_raw(public_key, data, sig):
        print("ASSINATURA VÁLIDA")
        return 0
    print("ASSINATURA INVÁLIDA — o manifest foi alterado ou a chave não corresponde")
    return 1


def cmd_pubid(public_b64: str) -> int:
    print(_kid_from_spki(_spki(_load_public(public_b64))))
    return 0


def _read_key_from_env_or_prompt(var: str = "DROPS_PRIVATE_KEY") -> str:
    import os

    value = os.environ.get(var, "").strip()
    if value:
        return value
    value = input(f"{var} (base64): ").strip()
    if not value:
        raise SystemExit(f"informe {var}")
    return value


USAGE = __doc__


def main(argv) -> int:
    if len(argv) < 2:
        print(USAGE)
        return 2
    cmd, args = argv[1], argv[2:]
    if cmd == "gen":
        return cmd_gen(args[0] if args else "")
    if cmd == "sign" and len(args) == 2:
        return cmd_sign(args[0], args[1])
    if cmd == "verify" and len(args) == 2:
        return cmd_verify(args[0], args[1])
    if cmd == "pubid" and len(args) == 1:
        return cmd_pubid(args[0])
    print(USAGE)
    return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
