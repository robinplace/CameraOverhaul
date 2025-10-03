#!zsh

set -e
cd "$(git rev-parse --show-toplevel)"

print_help() {
	cat <<help
usage: ./t [options] [command]

options:
  -h, --help  print this help message

commands:
	build       build all packages
	start       start timberborn
	restart     restart timberborn, if it is running
help
}
zparseopts -D -- h=help -help=help
if [[ -v help[1] ]]; then print_help; exit; fi

case "$1" in
	"build")
	;;"start")
		echo "starting"
		/Applications/Steam.app/Contents/MacOS/steam_osx -applaunch 1062090 -skipModManager
	;;"restart")
		echo "restarting"
		killall Timberborn && ./start || true
	;;*)
		print_help && exit 1
esac
