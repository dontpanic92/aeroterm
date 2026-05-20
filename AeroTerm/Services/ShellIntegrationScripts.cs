// <copyright file="ShellIntegrationScripts.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// In-process source of the shell-integration scripts that
/// <see cref="ShellIntegrationInjector"/> writes to disk before launching
/// a child shell. Inlined as string constants (instead of embedded
/// resources) so PublishAot works without bookkeeping the resource
/// manifest, and so callers can substitute paths at write time.
/// </summary>
/// <remarks>
/// The integration scripts emit OSC 133 prompt marks per the de-facto
/// FinalTerm / VS Code conventions:
/// <list type="bullet">
///   <item><description><c>OSC 133 ; A</c> — start of prompt.</description></item>
///   <item><description><c>OSC 133 ; B</c> — end of prompt; user input begins.</description></item>
///   <item><description><c>OSC 133 ; C</c> — command starts executing.</description></item>
///   <item><description><c>OSC 133 ; D ; &lt;exit&gt;</c> — command finished.</description></item>
/// </list>
/// </remarks>
internal static class ShellIntegrationScripts
{
    /// <summary>
    /// Token the shim files use as a placeholder for the absolute path to
    /// the integration script on disk; substituted via plain string
    /// replace at write time.
    /// </summary>
    public const string IntegrationPathPlaceholder = "@AEROTERM_INTEGRATION_SCRIPT@";

    /// <summary>
    /// zsh integration: emits OSC 133 marks via <c>precmd</c> /
    /// <c>preexec</c> hooks and a PS1 suffix. Loaded once per shell;
    /// re-entry (e.g. <c>exec zsh</c>) is guarded by a sentinel variable.
    /// </summary>
    public const string ZshIntegration = """
# AeroTerm shell integration (zsh).
# Emits OSC 133 prompt marks so the host terminal can identify prompt /
# user-input / command-output regions. Safe to source multiple times.

if [[ -n ${AEROTERM_SHELL_INTEGRATION_LOADED:-} ]]; then
    return 0
fi
AEROTERM_SHELL_INTEGRATION_LOADED=1

__aeroterm_osc133_a() { print -n '\e]133;A\a'; }
__aeroterm_osc133_b() { print -n '\e]133;B\a'; }
__aeroterm_osc133_c() { print -n '\e]133;C\a'; }
__aeroterm_osc133_d() { print -n "\e]133;D;${1:-0}\a"; }
__aeroterm_urlencode_path() {
    emulate -L zsh
    local LC_ALL=C s="$1" i c out=""
    for (( i = 1; i <= ${#s}; i++ )); do
        c="${s[i]}"
        case "$c" in
            [a-zA-Z0-9.~_/-]) out="${out}${c}" ;;
            *) out="${out}$(printf '%%%02X' "'$c")" ;;
        esac
    done
    print -rn -- "$out"
}
__aeroterm_osc7_cwd() {
    print -n "\e]7;file://${HOSTNAME:-localhost}$(__aeroterm_urlencode_path "$PWD")\a"
}

# Pre-prompt hook: emit D for the just-finished command (with its exit
# code) and A for the new prompt about to render.
__aeroterm_precmd() {
    local exit=$?
    __aeroterm_osc133_d "$exit"
    __aeroterm_osc7_cwd
    __aeroterm_osc133_a
    return $exit
}

# Pre-exec hook: emit C just before the user's command runs.
__aeroterm_preexec() {
    __aeroterm_osc133_c
}

# Hook into zsh's standard arrays. autoload -Uz add-zsh-hook is the
# canonical entry point but is not always available in stripped envs;
# fall back to direct array append.
if (( $+functions[add-zsh-hook] )); then
    add-zsh-hook precmd __aeroterm_precmd
    add-zsh-hook preexec __aeroterm_preexec
else
    autoload -Uz add-zsh-hook 2>/dev/null && {
        add-zsh-hook precmd __aeroterm_precmd
        add-zsh-hook preexec __aeroterm_preexec
    } || {
        precmd_functions=(__aeroterm_precmd ${precmd_functions:#__aeroterm_precmd})
        preexec_functions=(__aeroterm_preexec ${preexec_functions:#__aeroterm_preexec})
    }
fi

# Append the B mark to the prompt. %{ %} brackets keep zsh's prompt
# width calculation correct (the OSC sequence prints zero columns).
PS1="${PS1}%{$(printf '\e]133;B\a')%}"

# Final A for the very first prompt: precmd will fire before later
# prompts, but the first prompt is already mid-render by the time we
# load. Emit A now so the first command's input region is well-formed.
__aeroterm_osc7_cwd
__aeroterm_osc133_a
""";

    /// <summary>
    /// bash integration: emits OSC 133 marks via <c>PROMPT_COMMAND</c>
    /// and a <c>DEBUG</c> trap. The DEBUG trap fires for every command,
    /// including those generated from <c>PROMPT_COMMAND</c>; we suppress
    /// those so a single user command produces exactly one C mark.
    /// </summary>
    public const string BashIntegration = """
# AeroTerm shell integration (bash).

if [[ -n ${AEROTERM_SHELL_INTEGRATION_LOADED:-} ]]; then
    return 0
fi
AEROTERM_SHELL_INTEGRATION_LOADED=1

__aeroterm_in_prompt=1

__aeroterm_urlencode_path() {
    local LC_ALL=C s="$1" i c out=""
    for (( i = 0; i < ${#s}; i++ )); do
        c="${s:i:1}"
        case "$c" in
            [a-zA-Z0-9.~_/-]) out="${out}${c}" ;;
            *) out="${out}$(printf '%%%02X' "'$c")" ;;
        esac
    done
    printf '%s' "$out"
}

__aeroterm_osc7_cwd() {
    printf '\e]7;file://%s%s\a' "${HOSTNAME:-localhost}" "$(__aeroterm_urlencode_path "$PWD")"
}

__aeroterm_precmd() {
    local exit=$?
    printf '\e]133;D;%s\a' "$exit"
    __aeroterm_osc7_cwd
    printf '\e]133;A\a'
    __aeroterm_in_prompt=1
    return $exit
}

__aeroterm_preexec() {
    # DEBUG fires for every simple command, including pieces of
    # PROMPT_COMMAND. Only emit C for the first command after the
    # prompt was last printed.
    if [[ -n ${COMP_LINE:-} ]]; then
        return 0
    fi
    if (( __aeroterm_in_prompt )); then
        __aeroterm_in_prompt=0
        printf '\e]133;C\a'
    fi
}

# Append B to PS1. \[ \] tells bash these bytes do not advance the cursor.
PS1="${PS1}\[$(printf '\e]133;B\a')\]"

# PROMPT_COMMAND can be either a string (older bash) or an array
# (bash 5.1+). Handle both.
if [[ ${BASH_VERSINFO[0]} -gt 5 || ( ${BASH_VERSINFO[0]} -eq 5 && ${BASH_VERSINFO[1]} -ge 1 ) ]]; then
    PROMPT_COMMAND=(__aeroterm_precmd "${PROMPT_COMMAND[@]}")
else
    if [[ -z ${PROMPT_COMMAND:-} ]]; then
        PROMPT_COMMAND='__aeroterm_precmd'
    else
        PROMPT_COMMAND='__aeroterm_precmd; '"${PROMPT_COMMAND}"
    fi
fi

trap '__aeroterm_preexec' DEBUG
""";

    /// <summary>
    /// PowerShell integration. Wraps the user's existing <c>prompt</c>
    /// function so each invocation emits the prompt-start / prompt-end /
    /// command-finished marks. Pre-exec marks are emitted via the
    /// <c>PSConsoleHostReadLine</c> wrapper (PSReadLine).
    /// </summary>
    public const string PowerShellIntegration = """
# AeroTerm shell integration (PowerShell).

if ($env:AEROTERM_SHELL_INTEGRATION_LOADED) { return }
$env:AEROTERM_SHELL_INTEGRATION_LOADED = '1'

# Use [char]27 / [char]7 instead of the `e / `a escapes: those literal
# escape forms were only introduced in PowerShell 6, and AeroTerm also
# targets Windows PowerShell 5.1 (the default `powershell.exe` shipped
# with Windows 10/11). On 5.1 `e becomes a plain "e" character, so the
# OSC 133 marks would be printed verbatim into the user's prompt.
$global:__AeroTermEsc = [char]27
$global:__AeroTermBel = [char]7

$global:__AeroTermLastExitCode = 0

if (-not (Test-Path Function:\__AeroTerm_OriginalPrompt)) {
    Copy-Item Function:\prompt Function:\__AeroTerm_OriginalPrompt -ErrorAction SilentlyContinue
}

function global:prompt {
    $exit = if ($?) { 0 } else { if ($LASTEXITCODE) { $LASTEXITCODE } else { 1 } }
    $esc = $global:__AeroTermEsc
    $bel = $global:__AeroTermBel
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append("$esc]133;D;$exit$bel")
    $cwdPath = $executionContext.SessionState.Path.CurrentLocation.ProviderPath
    if ($cwdPath) {
        $cwdUri = [System.Uri]::new($cwdPath).AbsoluteUri
        [void]$sb.Append("$esc]7;$cwdUri$bel")
    }
    [void]$sb.Append("$esc]133;A$bel")
    if (Test-Path Function:\__AeroTerm_OriginalPrompt) {
        [void]$sb.Append((& __AeroTerm_OriginalPrompt))
    } else {
        [void]$sb.Append("PS $($executionContext.SessionState.Path.CurrentLocation)$('>' * ($nestedPromptLevel + 1)) ")
    }
    [void]$sb.Append("$esc]133;B$bel")
    $sb.ToString()
}

if (Get-Module -ListAvailable PSReadLine) {
    if (-not (Test-Path Function:\__AeroTerm_OriginalReadLine)) {
        if (Test-Path Function:\PSConsoleHostReadLine) {
            Copy-Item Function:\PSConsoleHostReadLine Function:\__AeroTerm_OriginalReadLine -ErrorAction SilentlyContinue
        }
    }
    function global:PSConsoleHostReadLine {
        $line = if (Test-Path Function:\__AeroTerm_OriginalReadLine) {
            & __AeroTerm_OriginalReadLine
        } else {
            [Microsoft.PowerShell.PSConsoleReadLine]::ReadLine($Host.Runspace, $ExecutionContext)
        }
        [Console]::Write("$($global:__AeroTermEsc)]133;C$($global:__AeroTermBel)")
        $line
    }
}
""";

    /// <summary>
    /// fish integration. fish 3+ supports event handlers for
    /// <c>fish_prompt</c> and <c>fish_preexec</c>; OSC 133 marks plug in
    /// cleanly without monkey-patching <c>fish_prompt</c>.
    /// </summary>
    public const string FishIntegration = """
# AeroTerm shell integration (fish).

if set -q AEROTERM_SHELL_INTEGRATION_LOADED
    exit 0
end
set -gx AEROTERM_SHELL_INTEGRATION_LOADED 1

function __aeroterm_urlencode_path
    set -l parts (string split / -- $argv[1])
    set -l out ''
    for i in (seq (count $parts))
        set -l part (string escape --style=url -- $parts[$i])
        if test $i -eq 1
            set out $part
        else
            set out "$out/$part"
        end
    end
    printf '%s' $out
end

function __aeroterm_osc7_cwd
    printf '\e]7;file://%s%s\a' (prompt_hostname) (__aeroterm_urlencode_path $PWD)
end

function __aeroterm_emit_a --on-event fish_prompt
    set -l exit $status
    printf '\e]133;D;%s\a' $exit
    __aeroterm_osc7_cwd
    printf '\e]133;A\a'
end

function __aeroterm_emit_b --on-event fish_postcmd
    # fish_postcmd is undocumented in older fish; if absent the prompt
    # B is still appended via the prompt-end hook below.
end

function __aeroterm_emit_c --on-event fish_preexec
    printf '\e]133;C\a'
end

# Wrap fish_prompt so its rendered output is followed by B.
if not functions -q __aeroterm_original_fish_prompt
    if functions -q fish_prompt
        functions -c fish_prompt __aeroterm_original_fish_prompt
    else
        function __aeroterm_original_fish_prompt
            printf '%s@%s %s> ' $USER (prompt_hostname) (prompt_pwd)
        end
    end
    function fish_prompt
        __aeroterm_original_fish_prompt
        printf '\e]133;B\a'
    end
end
""";

    // ----- ZSH SHIM (ZDOTDIR override) -----
    // When AeroTerm sets ZDOTDIR=<shimDir>, zsh reads ALL its startup
    // files from that dir. Each shim file delegates to the user's
    // original startup file (located via $USER_ZDOTDIR or $HOME) and
    // then -- only in .zshrc, the interactive entry point -- sources
    // the integration script.

    /// <summary>zsh shim <c>.zshenv</c>.</summary>
    public const string ZshShimZshenv = """
# AeroTerm zsh shim. Sourced for every zsh invocation (login or not).
# Hands off to the user's real .zshenv before the integration script
# decides whether to load itself in .zshrc.

if [[ -n ${USER_ZDOTDIR:-} && -f "${USER_ZDOTDIR}/.zshenv" ]]; then
    ZDOTDIR=$USER_ZDOTDIR
    . "${USER_ZDOTDIR}/.zshenv"
elif [[ -f "${HOME}/.zshenv" ]]; then
    . "${HOME}/.zshenv"
fi
""";

    /// <summary>zsh shim <c>.zprofile</c>.</summary>
    public const string ZshShimZprofile = """
# AeroTerm zsh shim (login profile).
if [[ -n ${USER_ZDOTDIR:-} && -f "${USER_ZDOTDIR}/.zprofile" ]]; then
    ZDOTDIR=$USER_ZDOTDIR
    . "${USER_ZDOTDIR}/.zprofile"
elif [[ -f "${HOME}/.zprofile" ]]; then
    . "${HOME}/.zprofile"
fi
""";

    /// <summary>
    /// zsh shim <c>.zshrc</c>. Sources the user's interactive startup
    /// file then loads the AeroTerm integration script. The integration
    /// path is substituted at write time.
    /// </summary>
    public const string ZshShimZshrc = """
# AeroTerm zsh shim (interactive entry point).
__AEROTERM_USER_ZDOTDIR=${USER_ZDOTDIR:-$HOME}
if [[ -f "${__AEROTERM_USER_ZDOTDIR}/.zshrc" ]]; then
    ZDOTDIR=${__AEROTERM_USER_ZDOTDIR}
    . "${__AEROTERM_USER_ZDOTDIR}/.zshrc"
fi

# Restore ZDOTDIR so child processes see the user's view, not ours.
if [[ -n ${USER_ZDOTDIR:-} ]]; then
    ZDOTDIR=$USER_ZDOTDIR
else
    unset ZDOTDIR
fi
unset USER_ZDOTDIR

# Load the AeroTerm OSC 133 integration. Skipping is safe -- the host
# terminal will simply fall back to "no shell integration" mode.
if [[ -r '@AEROTERM_INTEGRATION_SCRIPT@' ]]; then
    . '@AEROTERM_INTEGRATION_SCRIPT@'
fi
unset __AEROTERM_USER_ZDOTDIR
""";

    /// <summary>zsh shim <c>.zlogin</c>.</summary>
    public const string ZshShimZlogin = """
# AeroTerm zsh shim (login).
if [[ -n ${USER_ZDOTDIR:-} && -f "${USER_ZDOTDIR}/.zlogin" ]]; then
    ZDOTDIR=$USER_ZDOTDIR
    . "${USER_ZDOTDIR}/.zlogin"
elif [[ -f "${HOME}/.zlogin" ]]; then
    . "${HOME}/.zlogin"
fi
""";

    /// <summary>
    /// bash shim rcfile. Used with <c>bash --rcfile &lt;path&gt; -i</c>.
    /// Sources the user's <c>~/.bashrc</c> (and <c>~/.bash_profile</c>
    /// when the parent shell would have been a login shell) then loads
    /// the integration script.
    /// </summary>
    public const string BashShimRcfile = """
# AeroTerm bash shim. Hands off to the user's normal startup files,
# then loads the AeroTerm OSC 133 integration.

if [[ -n ${AEROTERM_BASH_LOGIN:-} ]]; then
    if [[ -f "$HOME/.bash_profile" ]]; then
        . "$HOME/.bash_profile"
    elif [[ -f "$HOME/.bash_login" ]]; then
        . "$HOME/.bash_login"
    elif [[ -f "$HOME/.profile" ]]; then
        . "$HOME/.profile"
    fi
    unset AEROTERM_BASH_LOGIN
else
    if [[ -f "$HOME/.bashrc" ]]; then
        . "$HOME/.bashrc"
    fi
fi

if [[ -r '@AEROTERM_INTEGRATION_SCRIPT@' ]]; then
    . '@AEROTERM_INTEGRATION_SCRIPT@'
fi
""";
}
