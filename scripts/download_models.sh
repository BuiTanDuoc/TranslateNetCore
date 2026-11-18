#!/usr/bin/env bash
# Tải model OPUS-MT đã export sẵn sang ONNX từ Hugging Face cho NetTranslate (không cần Python).
#
#   ./scripts/download_models.sh en-vi vi-en
#   QUANTIZED=1 ./scripts/download_models.sh en-vi vi-en    # bản int8, nhẹ hơn ~4 lần
#   FORCE=1 ./scripts/download_models.sh en-vi              # tải lại
set -uo pipefail

HF="https://huggingface.co"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUT="${OUT:-$SCRIPT_DIR/../models}"
SUFFIX=""
[ "${QUANTIZED:-0}" = "1" ] && SUFFIX="_quantized"
PAIRS=("$@")
[ ${#PAIRS[@]} -eq 0 ] && PAIRS=(en-vi vi-en)

target_prefix() {
  case "$1" in
    en-zh) echo ">>cmn_Hans<<" ;;
    *) echo "" ;;
  esac
}

# get <required 0|1> <remote path> <dest> <repo>...
get() {
  local required="$1" remote="$2" dest="$3"; shift 3
  local name; name="$(basename "$dest")"
  if [ -s "$dest" ] && [ "${FORCE:-0}" != "1" ]; then echo "    = $name"; return 0; fi
  for repo in "$@"; do
    if curl -fsSL --retry 3 -o "$dest.part" "$HF/$repo/resolve/main/$remote"; then
      mv -f "$dest.part" "$dest"
      echo "    + $name ($(du -h "$dest" | cut -f1)) <- $repo"
      return 0
    fi
  done
  rm -f "$dest.part"
  if [ "$required" = "1" ]; then echo "    ! không tải được $remote" >&2; return 1; fi
  echo "    - $name (không có, bỏ qua)"
  return 0
}

failed=()
for pair in "${PAIRS[@]}"; do
  if ! [[ "$pair" =~ ^[a-z]{2,3}-[a-z]{2,3}$ ]]; then echo "Bỏ qua '$pair' (định dạng phải như en-vi)" >&2; continue; fi

  dir="$OUT/$pair"
  mkdir -p "$dir"
  echo "[$pair] -> $dir"
  onnx_repos=("Xenova/opus-mt-$pair" "onnx-community/opus-mt-$pair")
  tok_repos=("${onnx_repos[@]}" "Helsinki-NLP/opus-mt-$pair")

  ok=1
  for f in source.spm vocab.json config.json; do get 1 "$f" "$dir/$f" "${tok_repos[@]}" || ok=0; done
  for f in target.spm generation_config.json; do get 0 "$f" "$dir/$f" "${tok_repos[@]}"; done
  get 1 "onnx/encoder_model$SUFFIX.onnx" "$dir/encoder_model$SUFFIX.onnx" "${onnx_repos[@]}" || ok=0
  get 1 "onnx/decoder_model$SUFFIX.onnx" "$dir/decoder_model$SUFFIX.onnx" "${onnx_repos[@]}" || ok=0
  get 0 "onnx/decoder_with_past_model$SUFFIX.onnx" "$dir/decoder_with_past_model$SUFFIX.onnx" "${onnx_repos[@]}"

  src="${pair%%-*}"; tgt="${pair#*-}"; prefix="$(target_prefix "$pair")"
  if [ -n "$prefix" ]; then
    printf '{\n  "source": "%s",\n  "target": "%s",\n  "targetPrefix": "%s"\n}\n' "$src" "$tgt" "$prefix" > "$dir/model.json"
  else
    printf '{\n  "source": "%s",\n  "target": "%s"\n}\n' "$src" "$tgt" > "$dir/model.json"
  fi

  if [ "$ok" = "1" ]; then echo "[$pair] xong"; echo; else failed+=("$pair"); fi
done

if [ ${#failed[@]} -gt 0 ]; then echo "Không tải được: ${failed[*]}" >&2; exit 1; fi
echo "Hoàn tất. Khởi động lại NetTranslate hoặc gọi POST /admin/models/reload."
